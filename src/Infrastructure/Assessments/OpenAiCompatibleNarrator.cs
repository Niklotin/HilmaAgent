using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using Microsoft.Extensions.Logging;

namespace HilmaAgent.Infrastructure.Assessments;

/// <param name="Model">Pinned model identifier, recorded on every assessment it writes.</param>
/// <param name="ApiKey">Null is legitimate: a model on your own machine usually needs no key.</param>
/// <param name="BaseUrl">Endpoint root, e.g. <c>http://localhost:11434/v1</c> for Ollama.</param>
public sealed record NarratorSettings(string Model, string? ApiKey, string BaseUrl, int MaxSources);

/// <summary>
/// Narrates through anything speaking the OpenAI chat-completions API.
/// </summary>
/// <remarks>
/// <para>One implementation covers OpenAI, Azure OpenAI, OpenRouter, Together — and, the reason it
/// exists, Ollama, LM Studio or vLLM running on your own machine. They differ by base URL and model
/// name, not by protocol, so a second vendor SDK would buy nothing.</para>
/// <para>It asks for <c>response_format: json_object</c> where the server supports it and falls back
/// to parsing a fenced reply where it does not, because small local models frequently ignore the
/// hint. That tolerance lives in <see cref="NarrationPrompt.Parse"/> so every provider inherits it.</para>
/// </remarks>
public class OpenAiCompatibleNarrator(
    HttpClient http,
    NarratorSettings settings,
    ILogger logger) : IAssessmentNarrator
{
    public string ModelId => settings.Model;

    public async Task<Narration> NarrateAsync(
        Notice notice,
        CompanyProfile profile,
        ScoreBreakdown breakdown,
        IReadOnlyList<NarrationSource> sources,
        CancellationToken ct = default)
    {
        var trimmed = sources.Take(settings.MaxSources).ToList();

        var request = new
        {
            model = settings.Model,
            messages = new object[]
            {
                new { role = "system", content = NarrationPrompt.System },
                new { role = "user", content = NarrationPrompt.BuildUser(notice, profile, breakdown, trimmed) },
            },
            // Deterministic-ish: the score is already fixed, and the prose should not wander between
            // two runs of the same notice any more than it has to.
            temperature = 0.2,
            response_format = new { type = "json_object" },
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, $"{settings.BaseUrl.TrimEnd('/')}/chat/completions")
        {
            Content = JsonContent.Create(request),
        };

        // A local model typically wants no key at all, so an absent one is not an error here.
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        using var response = await http.SendAsync(message, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"{settings.Model} returned {(int)response.StatusCode}: {NarrationPrompt.Truncate(error, 500)}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;

        var text = root.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()
            ?? throw new InvalidOperationException($"{settings.Model} returned an empty response.");

        if (root.TryGetProperty("usage", out var usage))
            logger.LogInformation("{Model}: {In} prompt tokens, {Out} completion tokens.",
                settings.Model,
                usage.TryGetProperty("prompt_tokens", out var i) ? i.GetInt32() : 0,
                usage.TryGetProperty("completion_tokens", out var o) ? o.GetInt32() : 0);

        return NarrationPrompt.Parse(text, trimmed);
    }
}
