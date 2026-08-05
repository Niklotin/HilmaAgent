using System.Net.Http.Json;
using System.Text.Json;
using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using Microsoft.Extensions.Logging;

namespace HilmaAgent.Infrastructure.Assessments;

/// <summary>
/// Narrates an assessment with Google Gemini, constrained by a response schema.
/// </summary>
/// <remarks>
/// The prompt hands the model a finished score and forbids it from producing one. Structured output
/// pins the shape so the recommendation is one of three enum values rather than prose that has to be
/// parsed, and citations come back as identifiers rather than as quotations the model could invent.
/// </remarks>
public class GeminiAssessmentNarrator(
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
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
            throw new InvalidOperationException("A Gemini API key is required to produce assessments.");

        var trimmed = sources.Take(settings.MaxSources).ToList();
        var request = BuildRequest(notice, profile, breakdown, trimmed);

        var url = $"{settings.BaseUrl.TrimEnd('/')}/models/{settings.Model}:generateContent?key={settings.ApiKey}";
        using var response = await http.PostAsJsonAsync(url, request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Gemini returned {(int)response.StatusCode}: {NarrationPrompt.Truncate(error, 500)}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;

        var text = root.GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Gemini returned an empty response.");

        if (root.TryGetProperty("usageMetadata", out var usage))
            logger.LogInformation("Gemini {Model}: {In} prompt tokens, {Out} output tokens.",
                settings.Model,
                usage.TryGetProperty("promptTokenCount", out var i) ? i.GetInt32() : 0,
                usage.TryGetProperty("candidatesTokenCount", out var o) ? o.GetInt32() : 0);

        return NarrationPrompt.Parse(text, trimmed);
    }

    private object BuildRequest(
        Notice notice,
        CompanyProfile profile,
        ScoreBreakdown breakdown,
        IReadOnlyList<NarrationSource> sources) => new
        {
            systemInstruction = new { parts = new[] { new { text = NarrationPrompt.System } } },
            contents = new[] { new { parts = new[] { new { text = NarrationPrompt.BuildUser(notice, profile, breakdown, sources) } } } },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = new
                {
                    type = "OBJECT",
                    properties = new
                    {
                        recommendation = new { type = "STRING", @enum = new[] { Recommendation.Go, Recommendation.NoGo, Recommendation.Investigate } },
                        reasoning = new { type = "STRING" },
                        citedChunkIds = new { type = "ARRAY", items = new { type = "STRING" } },
                    },
                    required = new[] { "recommendation", "reasoning", "citedChunkIds" },
                },
            },
        };

}
