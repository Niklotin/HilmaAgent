using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Providers;
using HilmaAgent.Infrastructure.Persistence;
using HilmaAgent.Infrastructure.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HilmaAgent.Infrastructure.Assessments;

/// <param name="Provider">See <see cref="ProviderKeys"/>.</param>
/// <param name="Model">What the provider is currently pinned to.</param>
/// <param name="Ready">Whether it could actually run — a provider with no key is not.</param>
/// <param name="Reason">
/// A stable code, not prose: the UI ships in two languages, and a sentence written here could only
/// ever be in one of them. Unlike the score breakdown, nothing about this is stored, so it costs
/// nothing to keep it machine-readable.
/// </param>
public sealed record NarratorOption(string Provider, string Model, bool Ready, string? Reason);

public static class NarratorNotReady
{
    public const string NoApiKey = "no-api-key";
    public const string NoEndpoint = "no-endpoint";
}

/// <summary>
/// Builds the narrator for a run, from whatever the operator has configured.
/// </summary>
/// <remarks>
/// <para>The narrator used to be a startup singleton, which meant changing models meant changing
/// configuration and restarting. It is resolved per assessment instead — but resolved <em>from a
/// stored setting</em>, not chosen by the caller, so an assessment cannot quietly be run against a
/// weaker model to get a different answer.</para>
/// <para><see cref="IAssessmentNarrator"/> itself is unchanged: whatever is built here still
/// receives a finished <see cref="ScoreBreakdown"/> and has no way to alter it. Swapping providers
/// changes the prose, never the ranking.</para>
/// </remarks>
public class NarratorRegistry(
    HilmaDbContext db,
    ProviderCredentialStore credentials,
    IHttpClientFactory clients,
    IOptions<GeminiOptions> geminiOptions,
    ILoggerFactory loggerFactory) : INarratorRegistry
{
    private readonly GeminiOptions _gemini = geminiOptions.Value;

    /// <summary>Environment-supplied keys, the bootstrap path a stored credential overrides.</summary>
    private IReadOnlyDictionary<string, string?> EnvironmentKeys => new Dictionary<string, string?>
    {
        [ProviderKeys.Gemini] = _gemini.ApiKey,
        [ProviderKeys.OpenAiCompatible] = null,
    };

    public async Task<string> ActiveProviderAsync(CancellationToken ct = default)
    {
        var stored = await db.AppSettings.AsNoTracking()
            .Where(s => s.Key == SettingKeys.NarratorProvider)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);

        return ProviderKeys.IsValid(stored) ? stored! : ProviderKeys.Gemini;
    }

    public async Task SetActiveProviderAsync(string provider, CancellationToken ct = default)
    {
        if (!ProviderKeys.IsValid(provider))
            throw new ArgumentException($"Unknown provider '{provider}'.", nameof(provider));

        var setting = await db.AppSettings.FirstOrDefaultAsync(s => s.Key == SettingKeys.NarratorProvider, ct);
        if (setting is null)
        {
            setting = new AppSetting { Key = SettingKeys.NarratorProvider };
            db.AppSettings.Add(setting);
        }

        setting.Value = provider;
        setting.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>What the settings screen offers, and which of it would actually work.</summary>
    public async Task<IReadOnlyList<NarratorOption>> OptionsAsync(CancellationToken ct = default)
    {
        var options = new List<NarratorOption>();

        foreach (var provider in ProviderKeys.All)
        {
            var settings = await SettingsForAsync(provider, ct);

            var (ready, reason) = provider switch
            {
                ProviderKeys.Gemini when string.IsNullOrWhiteSpace(settings.ApiKey) => (false, NarratorNotReady.NoApiKey),
                // A local model legitimately has no key, so only the endpoint is required here.
                ProviderKeys.OpenAiCompatible when string.IsNullOrWhiteSpace(settings.BaseUrl) => (false, NarratorNotReady.NoEndpoint),
                _ => (true, (string?)null),
            };

            options.Add(new NarratorOption(provider, settings.Model, ready, reason));
        }

        return options;
    }

    public async Task<IAssessmentNarrator> ResolveAsync(CancellationToken ct = default)
    {
        var provider = await ActiveProviderAsync(ct);
        var settings = await SettingsForAsync(provider, ct);

        return provider switch
        {
            ProviderKeys.OpenAiCompatible => new OpenAiCompatibleNarrator(
                clients.CreateClient(ProviderKeys.OpenAiCompatible),
                settings,
                loggerFactory.CreateLogger<OpenAiCompatibleNarrator>()),

            _ => new GeminiAssessmentNarrator(
                clients.CreateClient(ProviderKeys.Gemini),
                settings,
                loggerFactory.CreateLogger<GeminiAssessmentNarrator>()),
        };
    }

    private async Task<NarratorSettings> SettingsForAsync(string provider, CancellationToken ct)
    {
        var resolved = await credentials.ResolveAsync(provider, EnvironmentKeys.GetValueOrDefault(provider), ct);

        return provider switch
        {
            ProviderKeys.OpenAiCompatible => new NarratorSettings(
                resolved.Model ?? "gpt-4o-mini",
                resolved.ApiKey,
                resolved.BaseUrl ?? "",
                _gemini.MaxSources),

            _ => new NarratorSettings(
                resolved.Model ?? _gemini.Model,
                resolved.ApiKey,
                resolved.BaseUrl ?? _gemini.BaseUrl,
                _gemini.MaxSources),
        };
    }
}
