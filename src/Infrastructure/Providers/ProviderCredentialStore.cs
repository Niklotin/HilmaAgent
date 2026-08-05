using HilmaAgent.Core.Providers;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HilmaAgent.Infrastructure.Providers;

/// <param name="Provider">See <see cref="ProviderKeys"/>.</param>
/// <param name="HasKey">Whether a key is stored. The key itself is never returned.</param>
/// <param name="KeyHint">Last four characters, enough to tell two keys apart.</param>
/// <param name="KeyUnreadable">
/// True when a key is stored but could not be decrypted — the data-protection key ring changed.
/// Surfaced rather than swallowed, because the failure otherwise looks like an authentication error
/// from the provider and sends you looking in the wrong place.
/// </param>
public sealed record ProviderStatus(
    string Provider,
    bool HasKey,
    string? KeyHint,
    string? BaseUrl,
    string? Model,
    bool KeyUnreadable,
    bool ConfiguredFromEnvironment,
    DateTimeOffset? UpdatedAt,
    string? UpdatedBy);

/// <summary>
/// Reads and writes provider credentials, and is the only thing that ever sees a key in the clear.
/// </summary>
/// <remarks>
/// <para>Keys are encrypted with the ASP.NET Data Protection key ring, which is itself persisted in
/// Postgres — so a container rebuild does not orphan every stored credential.</para>
/// <para><b>The read path deliberately cannot return a key.</b> <see cref="GetStatusesAsync"/> is
/// what the API exposes and it yields a hint at most; <see cref="ResolveAsync"/> returns the real
/// value and is only ever called by the code that is about to make a request to that provider.</para>
/// </remarks>
public class ProviderCredentialStore(
    HilmaDbContext db,
    IDataProtectionProvider protection,
    ILogger<ProviderCredentialStore> logger)
{
    // Purpose string: changing it invalidates every stored key, so it is fixed forever.
    private readonly IDataProtector _protector = protection.CreateProtector("HilmaAgent.ProviderCredentials.v1");

    /// <summary>What the UI is allowed to know: which providers are configured, and by what.</summary>
    public async Task<IReadOnlyList<ProviderStatus>> GetStatusesAsync(
        IReadOnlyDictionary<string, string?> environmentKeys,
        CancellationToken ct = default)
    {
        var stored = await db.ProviderCredentials.AsNoTracking().ToDictionaryAsync(c => c.Provider, ct);

        return ProviderKeys.All.Select(provider =>
        {
            var fromEnvironment = environmentKeys.GetValueOrDefault(provider);

            if (!stored.TryGetValue(provider, out var credential))
                return new ProviderStatus(
                    provider,
                    HasKey: !string.IsNullOrWhiteSpace(fromEnvironment),
                    KeyHint: Hint(fromEnvironment),
                    BaseUrl: null,
                    Model: null,
                    KeyUnreadable: false,
                    ConfiguredFromEnvironment: !string.IsNullOrWhiteSpace(fromEnvironment),
                    UpdatedAt: null,
                    UpdatedBy: null);

            var unreadable = credential.ProtectedApiKey is not null && TryUnprotect(credential.ProtectedApiKey) is null;

            return new ProviderStatus(
                provider,
                HasKey: credential.ProtectedApiKey is not null || !string.IsNullOrWhiteSpace(fromEnvironment),
                KeyHint: credential.KeyHint ?? Hint(fromEnvironment),
                credential.BaseUrl,
                credential.Model,
                KeyUnreadable: unreadable,
                ConfiguredFromEnvironment: credential.ProtectedApiKey is null && !string.IsNullOrWhiteSpace(fromEnvironment),
                credential.UpdatedAt,
                credential.UpdatedBy);
        }).ToList();
    }

    /// <summary>
    /// The credential to actually use, with the stored value taking precedence over the environment.
    /// </summary>
    /// <remarks>
    /// Stored wins because configuring at runtime is the point of the feature; the environment
    /// remains the way to bootstrap a fresh deployment and to run CI without a database write.
    /// </remarks>
    public async Task<ResolvedCredential> ResolveAsync(
        string provider,
        string? environmentKey,
        CancellationToken ct = default)
    {
        var credential = await db.ProviderCredentials.AsNoTracking()
            .FirstOrDefaultAsync(c => c.Provider == provider, ct);

        if (credential?.ProtectedApiKey is { } sealedKey)
        {
            var apiKey = TryUnprotect(sealedKey);
            if (apiKey is null)
                logger.LogError(
                    "Stored key for {Provider} could not be decrypted; the data-protection key ring has changed. Falling back to the environment.",
                    provider);
            else
                return new ResolvedCredential(apiKey, credential.BaseUrl, credential.Model);
        }

        return new ResolvedCredential(environmentKey, credential?.BaseUrl, credential?.Model);
    }

    public async Task SaveAsync(
        string provider,
        string? apiKey,
        string? baseUrl,
        string? model,
        string? updatedBy,
        CancellationToken ct = default)
    {
        var credential = await db.ProviderCredentials.FirstOrDefaultAsync(c => c.Provider == provider, ct);

        if (credential is null)
        {
            credential = new ProviderCredential { Provider = provider };
            db.ProviderCredentials.Add(credential);
        }

        // An omitted key leaves the stored one alone: the UI cannot read a key back, so re-saving an
        // endpoint or a model name must not require retyping the secret.
        if (apiKey is not null)
        {
            var trimmed = apiKey.Trim();
            if (trimmed.Length == 0)
            {
                credential.ProtectedApiKey = null;
                credential.KeyHint = null;
            }
            else
            {
                credential.ProtectedApiKey = _protector.Protect(trimmed);
                credential.KeyHint = Hint(trimmed);
            }
        }

        credential.BaseUrl = Blank(baseUrl);
        credential.Model = Blank(model);
        credential.UpdatedAt = DateTimeOffset.UtcNow;
        credential.UpdatedBy = Blank(updatedBy);

        await db.SaveChangesAsync(ct);

        // The value must never reach a log, so only the fact of the change is recorded.
        logger.LogInformation("Provider {Provider} updated by {Who}.", provider, credential.UpdatedBy ?? "unknown");
    }

    /// <summary>
    /// Whether saving this base URL would move the provider to a different endpoint.
    /// </summary>
    /// <remarks>
    /// Used to force the key to be re-entered when the destination changes. A stored key was given
    /// for a particular endpoint; carrying it over to a new one silently is how a key ends up being
    /// sent somewhere its owner never intended.
    /// </remarks>
    public async Task<bool> EndpointChangesAsync(string provider, string? baseUrl, CancellationToken ct = default)
    {
        var existing = await db.ProviderCredentials.AsNoTracking()
            .Where(c => c.Provider == provider)
            .Select(c => new { c.BaseUrl, HasKey = c.ProtectedApiKey != null })
            .FirstOrDefaultAsync(ct);

        if (existing is null || !existing.HasKey) return false;

        return !string.Equals(existing.BaseUrl, Blank(baseUrl), StringComparison.OrdinalIgnoreCase);
    }

    public async Task ForgetAsync(string provider, CancellationToken ct = default)
    {
        // Tracked delete rather than ExecuteDelete: this is one row keyed by provider, so the bulk
        // path buys nothing and costs the ability to test against the in-memory provider.
        var credential = await db.ProviderCredentials.FirstOrDefaultAsync(c => c.Provider == provider, ct);
        if (credential is null) return;

        db.ProviderCredentials.Remove(credential);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Provider {Provider} credentials removed.", provider);
    }

    private string? TryUnprotect(string sealedValue)
    {
        try
        {
            return _protector.Unprotect(sealedValue);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Stored credential could not be decrypted.");
            return null;
        }
    }

    private static string? Hint(string? key) =>
        string.IsNullOrWhiteSpace(key) || key.Length < 4 ? null : key[^4..];

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

/// <param name="ApiKey">The key in the clear. Never logged, never serialised into a response.</param>
public sealed record ResolvedCredential(string? ApiKey, string? BaseUrl, string? Model);
