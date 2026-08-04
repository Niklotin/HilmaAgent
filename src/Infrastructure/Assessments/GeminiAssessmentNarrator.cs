using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    IOptions<GeminiOptions> options,
    ILogger<GeminiAssessmentNarrator> logger) : IAssessmentNarrator
{
    private readonly GeminiOptions _options = options.Value;

    public string ModelId => _options.Model;

    public async Task<Narration> NarrateAsync(
        Notice notice,
        CompanyProfile profile,
        ScoreBreakdown breakdown,
        IReadOnlyList<NarrationSource> sources,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new InvalidOperationException("Gemini:ApiKey is required to produce assessments.");

        var trimmed = sources.Take(_options.MaxSources).ToList();
        var request = BuildRequest(notice, profile, breakdown, trimmed);

        var url = $"{_options.BaseUrl.TrimEnd('/')}/models/{_options.Model}:generateContent?key={_options.ApiKey}";
        using var response = await http.PostAsJsonAsync(url, request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException($"Gemini returned {(int)response.StatusCode}: {Truncate(error, 500)}");
        }

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;

        var text = root.GetProperty("candidates")[0].GetProperty("content")
            .GetProperty("parts")[0].GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Gemini returned an empty response.");

        if (root.TryGetProperty("usageMetadata", out var usage))
            logger.LogInformation("Gemini {Model}: {In} prompt tokens, {Out} output tokens.",
                _options.Model,
                usage.TryGetProperty("promptTokenCount", out var i) ? i.GetInt32() : 0,
                usage.TryGetProperty("candidatesTokenCount", out var o) ? o.GetInt32() : 0);

        return Parse(text, trimmed);
    }

    private static Narration Parse(string json, IReadOnlyList<NarrationSource> sources)
    {
        using var parsed = JsonDocument.Parse(json);
        var root = parsed.RootElement;

        var recommendation = root.TryGetProperty("recommendation", out var r) ? r.GetString() : null;
        if (!Recommendation.IsValid(recommendation))
            throw new InvalidOperationException($"Model returned an invalid recommendation: '{recommendation}'.");

        var reasoning = root.TryGetProperty("reasoning", out var reason) ? reason.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(reasoning))
            throw new InvalidOperationException("Model returned no reasoning.");

        // Citations are validated against what the model was actually given. A model that invents an
        // identifier gets it dropped rather than stored — an unverifiable citation is worse than none,
        // because it looks like evidence.
        var allowed = sources.Select(s => s.ChunkId).ToHashSet();
        var cited = new List<Guid>();

        if (root.TryGetProperty("citedChunkIds", out var ids) && ids.ValueKind == JsonValueKind.Array)
        {
            foreach (var id in ids.EnumerateArray())
            {
                if (Guid.TryParse(id.GetString(), out var parsedId) && allowed.Contains(parsedId) && !cited.Contains(parsedId))
                    cited.Add(parsedId);
            }
        }

        return new Narration(recommendation!, reasoning!, cited);
    }

    private object BuildRequest(
        Notice notice,
        CompanyProfile profile,
        ScoreBreakdown breakdown,
        IReadOnlyList<NarrationSource> sources) => new
        {
            systemInstruction = new { parts = new[] { new { text = SystemPrompt } } },
            contents = new[] { new { parts = new[] { new { text = BuildUserPrompt(notice, profile, breakdown, sources) } } } },
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

    private const string SystemPrompt =
        """
        Olet julkisten hankintojen tarjouskartoituksen avustaja. Tehtäväsi on selittää valmiiksi
        laskettu soveltuvuuspisteytys ja antaa suositus. Kirjoita suomeksi.

        Ehdottomat säännöt:
        - Pisteet on laskettu koodissa. Älä koskaan keksi, muuta tai arvioi uudelleen mitään lukua.
          Viittaa vain annettuihin pisteisiin ja sääntöihin.
        - Perustele jokainen väite annetuilla lähteillä. Käytä vain annettuja chunk-tunnisteita.
          Älä keksi tunnisteita äläkä viittaa tietoon, jota lähteissä ei ole.
        - Jos jokin este (gate) on lauennut, suositus on NO_GO.
        - Jos tietoa puuttuu (esimerkiksi hankinnan arvoa ei ole ilmoitettu), sano se suoraan
          sen sijaan että arvaisit.
        - Voit päätyä eri suositukseen kuin pisteytys ehdottaa, jos lähteissä on siihen selkeä syy.
          Perustele silloin ero täsmällisesti — ristiriita nostetaan ihmisen tarkastettavaksi.

        Kirjoita perustelu tiiviisti, 3–6 virkettä. Aloita johtopäätöksestä, sitten sen tueksi
        tärkeimmät seikat. Älä toista pistetaulukkoa sellaisenaan.
        """;

    private static string BuildUserPrompt(
        Notice notice,
        CompanyProfile profile,
        ScoreBreakdown breakdown,
        IReadOnlyList<NarrationSource> sources)
    {
        var builder = new StringBuilder();

        builder.AppendLine("## Yritysprofiili");
        builder.AppendLine($"Nimi: {profile.Name}");
        if (profile.Description is { } description) builder.AppendLine($"Kuvaus: {description}");
        builder.AppendLine($"Teknologiat: {string.Join(", ", profile.Technologies)}");
        builder.AppendLine($"CPV-koodit: {string.Join(", ", profile.PreferredCpvCodes)}");
        builder.AppendLine($"Alueet: {string.Join(", ", profile.Regions)}");
        builder.AppendLine($"Sopimusarvon haarukka: {profile.MinContractValue:N0} – {profile.MaxContractValue:N0} EUR");
        if (profile.ReferenceProjects.Count > 0)
        {
            builder.AppendLine("Referenssit:");
            foreach (var reference in profile.ReferenceProjects) builder.AppendLine($"- {reference}");
        }

        builder.AppendLine().AppendLine("## Ilmoitus");
        builder.AppendLine($"Tunniste: {notice.Id}");
        builder.AppendLine($"Otsikko: {notice.Title}");
        builder.AppendLine($"Hankintayksikkö: {notice.BuyerName}");
        builder.AppendLine($"Tyyppi: {notice.NoticeType}");
        builder.AppendLine($"CPV: {string.Join(", ", notice.CpvCodes)}");
        builder.AppendLine($"Alue: {string.Join(", ", notice.Region)}");
        builder.AppendLine($"Määräaika: {notice.SubmissionDeadline?.ToString("yyyy-MM-dd") ?? "ei ilmoitettu"}");

        builder.AppendLine().AppendLine("## Laskettu pisteytys (älä muuta näitä lukuja)");
        builder.AppendLine($"Kokonaispisteet: {breakdown.Total}/100");
        builder.AppendLine($"Pisteytyksen implikoima suositus: {Recommendation.FromScore(breakdown)}");
        foreach (var rule in breakdown.Rules)
            builder.AppendLine($"- {rule.Rule}: {rule.Awarded}/{rule.Max} — {rule.Detail}");

        if (breakdown.Gates.Count > 0)
        {
            builder.AppendLine("Esteet (pakottavat suosituksen NO_GO):");
            foreach (var gate in breakdown.Gates) builder.AppendLine($"- {gate.Gate}: {gate.Detail}");
        }

        if (breakdown.Warnings.Count > 0)
        {
            builder.AppendLine("Huomiot:");
            foreach (var warning in breakdown.Warnings) builder.AppendLine($"- {warning}");
        }

        builder.AppendLine().AppendLine("## Lähteet (viittaa näihin tunnisteilla)");
        foreach (var source in sources)
        {
            builder.AppendLine($"[{source.ChunkId}] ({source.Section}{(source.LotId is { } lot ? $", {lot}" : "")})");
            builder.AppendLine(Truncate(source.Content, 1500));
            builder.AppendLine();
        }

        return builder.ToString();
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
