using HilmaAgent.Core.Providers;
using HilmaAgent.Infrastructure.Persistence;
using HilmaAgent.Infrastructure.Providers;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace HilmaAgent.Tests;

/// <summary>
/// The store that holds provider API keys.
/// </summary>
/// <remarks>
/// These are security properties rather than features, which is exactly why they are tested: each
/// one is invisible when it works and expensive when it silently stops.
/// </remarks>
public class ProviderCredentialStoreTests
{
    private const string FakeKey = "sk-FAKE-not-a-real-key-9999";

    private static HilmaDbContext InMemoryDb() =>
        new(new DbContextOptionsBuilder<HilmaDbContext>()
            .UseInMemoryDatabase($"credentials-{Guid.NewGuid()}")
            .Options);

    private static ProviderCredentialStore Store(HilmaDbContext db, IDataProtectionProvider? protection = null) =>
        new(db, protection ?? DataProtectionProvider.Create(nameof(ProviderCredentialStoreTests)),
            NullLogger<ProviderCredentialStore>.Instance);

    private static readonly Dictionary<string, string?> NoEnvironment = new()
    {
        [ProviderKeys.Gemini] = null,
        [ProviderKeys.OpenAiCompatible] = null,
    };

    [Fact]
    public async Task The_key_is_never_stored_in_the_clear()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.Gemini, FakeKey, null, null, "Niko");

        var row = await db.ProviderCredentials.AsNoTracking().SingleAsync();

        row.ProtectedApiKey.ShouldNotBeNull();
        row.ProtectedApiKey.ShouldNotContain("sk-FAKE");
        row.ProtectedApiKey.ShouldNotBe(FakeKey);
    }

    [Fact]
    public async Task The_read_path_cannot_return_a_key()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.Gemini, FakeKey, null, null, "Niko");

        var statuses = await Store(db).GetStatusesAsync(NoEnvironment);
        var gemini = statuses.Single(s => s.Provider == ProviderKeys.Gemini);

        // A hint is enough to tell two keys apart and useless to anyone who intercepts it.
        gemini.HasKey.ShouldBeTrue();
        gemini.KeyHint.ShouldBe("9999");

        // Whatever else this record grows, none of it may ever be the key.
        gemini.ToString().ShouldNotContain("sk-FAKE");
    }

    [Fact]
    public async Task Resolving_returns_the_key_to_the_code_that_is_about_to_use_it()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.Gemini, FakeKey, null, null, null);

        var resolved = await Store(db).ResolveAsync(ProviderKeys.Gemini, environmentKey: null);

        resolved.ApiKey.ShouldBe(FakeKey);
    }

    [Fact]
    public async Task A_stored_key_beats_the_environment()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.Gemini, FakeKey, null, null, null);

        // Configuring at runtime is the point of the feature; the environment is the bootstrap path.
        (await Store(db).ResolveAsync(ProviderKeys.Gemini, "from-environment")).ApiKey.ShouldBe(FakeKey);
    }

    [Fact]
    public async Task The_environment_is_used_when_nothing_is_stored()
    {
        await using var db = InMemoryDb();

        (await Store(db).ResolveAsync(ProviderKeys.Gemini, "from-environment")).ApiKey.ShouldBe("from-environment");
    }

    [Fact]
    public async Task Omitting_the_key_leaves_the_stored_one_alone()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.Gemini, FakeKey, null, "gemini-3.6-flash", null);

        // The UI cannot read a key back, so changing a model name must not require retyping it.
        await Store(db).SaveAsync(ProviderKeys.Gemini, apiKey: null, null, "gemini-3.6-pro", null);

        var resolved = await Store(db).ResolveAsync(ProviderKeys.Gemini, null);
        resolved.ApiKey.ShouldBe(FakeKey);
        resolved.Model.ShouldBe("gemini-3.6-pro");
    }

    [Fact]
    public async Task An_empty_key_removes_the_stored_one()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.Gemini, FakeKey, null, null, null);

        await Store(db).SaveAsync(ProviderKeys.Gemini, apiKey: "", null, null, null);

        (await Store(db).ResolveAsync(ProviderKeys.Gemini, null)).ApiKey.ShouldBeNull();
    }

    /// <summary>
    /// Moving a provider to a different endpoint invalidates its key.
    /// </summary>
    /// <remarks>
    /// Without this, anyone who could reach the settings screen could repoint a provider at a server
    /// they control and the application would keep sending the stored key there.
    /// </remarks>
    [Fact]
    public async Task Changing_the_endpoint_is_reported_so_the_key_can_be_cleared()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.OpenAiCompatible, FakeKey, "http://localhost:11434/v1", null, null);

        (await Store(db).EndpointChangesAsync(ProviderKeys.OpenAiCompatible, "http://elsewhere.example/v1"))
            .ShouldBeTrue();

        // The same endpoint is not a move, so editing a model name does not cost a retype.
        (await Store(db).EndpointChangesAsync(ProviderKeys.OpenAiCompatible, "http://localhost:11434/v1"))
            .ShouldBeFalse();
    }

    [Fact]
    public async Task A_key_that_cannot_be_decrypted_is_reported_rather_than_swallowed()
    {
        await using var db = InMemoryDb();
        await Store(db, DataProtectionProvider.Create("original-key-ring"))
            .SaveAsync(ProviderKeys.Gemini, FakeKey, null, null, null);

        // A different key ring is what a rebuild used to cause when the keys lived on the container
        // filesystem. Silently falling back looks like a bad API key and sends you looking in the
        // wrong place, so it is surfaced instead.
        var statuses = await Store(db, DataProtectionProvider.Create("different-key-ring"))
            .GetStatusesAsync(NoEnvironment);

        statuses.Single(s => s.Provider == ProviderKeys.Gemini).KeyUnreadable.ShouldBeTrue();
    }

    [Fact]
    public async Task Forgetting_a_provider_removes_everything()
    {
        await using var db = InMemoryDb();
        await Store(db).SaveAsync(ProviderKeys.Gemini, FakeKey, null, null, null);

        await Store(db).ForgetAsync(ProviderKeys.Gemini);

        (await db.ProviderCredentials.CountAsync()).ShouldBe(0);
    }
}
