using HilmaAgent.Core.Providers;
using HilmaAgent.Infrastructure.Assessments;
using HilmaAgent.Infrastructure.Providers;
using Microsoft.Extensions.Options;

namespace HilmaAgent.Api.Notices;

/// <param name="ApiKey">
/// Omit to leave the stored key untouched; send an empty string to remove it. The key is never
/// returned by any endpoint, so editing an endpoint or model must not require retyping it.
/// </param>
public record ProviderRequest(string? ApiKey, string? BaseUrl, string? Model, string? UpdatedBy);

public record NarratorProviderRequest(string Provider);

public static class ProviderEndpoints
{
    public static IEndpointRouteBuilder MapProviderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/providers").WithTags("Providers");

        // What is configured — never the keys themselves. A four-character hint is enough to tell
        // two keys apart and useless to anyone who intercepts the response.
        group.MapGet("/", async (
            ProviderCredentialStore credentials,
            NarratorRegistry narrators,
            IOptions<GeminiOptions> gemini,
            CancellationToken ct) =>
        {
            var environment = new Dictionary<string, string?>
            {
                [ProviderKeys.Gemini] = gemini.Value.ApiKey,
                [ProviderKeys.OpenAiCompatible] = null,
            };

            return Results.Ok(new
            {
                active = await narrators.ActiveProviderAsync(ct),
                options = await narrators.OptionsAsync(ct),
                providers = await credentials.GetStatusesAsync(environment, ct),
            });
        })
        .WithName("GetProviders")
        .WithSummary("Which model providers are configured, and which could actually run. Never returns a key.");

        group.MapPut("/{provider}", async (
            string provider,
            ProviderRequest request,
            ProviderCredentialStore credentials,
            CancellationToken ct) =>
        {
            if (!ProviderKeys.IsValid(provider))
                return Results.BadRequest(new { error = $"Unknown provider '{provider}'." });

            if (request.BaseUrl is { } url && !string.IsNullOrWhiteSpace(url)
                && !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
                return Results.BadRequest(new { error = "BaseUrl must be an absolute URI." });

            // Repointing a provider at a different endpoint clears its stored key, so the key has to
            // be entered again for the new destination. Without this, anyone who could reach the
            // settings screen could redirect an existing key to a server they control.
            var movedEndpoint = await credentials.EndpointChangesAsync(provider, request.BaseUrl, ct);
            var apiKey = movedEndpoint && request.ApiKey is null ? string.Empty : request.ApiKey;

            await credentials.SaveAsync(provider, apiKey, request.BaseUrl, request.Model, request.UpdatedBy, ct);

            return Results.Ok(new { provider, keyClearedByEndpointChange = movedEndpoint && request.ApiKey is null });
        })
        .WithName("SaveProvider")
        .WithSummary("Stores a provider's key, endpoint and model. Changing the endpoint clears the key.");

        group.MapDelete("/{provider}", async (
            string provider,
            ProviderCredentialStore credentials,
            CancellationToken ct) =>
        {
            if (!ProviderKeys.IsValid(provider))
                return Results.BadRequest(new { error = $"Unknown provider '{provider}'." });

            await credentials.ForgetAsync(provider, ct);
            return Results.NoContent();
        })
        .WithName("ForgetProvider")
        .WithSummary("Removes a provider's stored credentials.");

        // Which provider narrates. A setting rather than a per-request argument, so an assessment
        // cannot quietly be run against a weaker model to get a different answer.
        group.MapPut("/active", async (
            NarratorProviderRequest request,
            NarratorRegistry narrators,
            CancellationToken ct) =>
        {
            if (!ProviderKeys.IsValid(request.Provider))
                return Results.BadRequest(new { error = $"Unknown provider '{request.Provider}'." });

            var options = await narrators.OptionsAsync(ct);
            var chosen = options.First(o => o.Provider == request.Provider);

            if (!chosen.Ready)
                return Results.BadRequest(new { error = $"'{request.Provider}' is not usable yet.", reason = chosen.Reason });

            await narrators.SetActiveProviderAsync(request.Provider, ct);
            return Results.Ok(new { active = request.Provider, model = chosen.Model });
        })
        .WithName("SetNarratorProvider")
        .WithSummary("Chooses which provider writes assessment narratives.");

        return app;
    }
}
