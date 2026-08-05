using System.Text;
using System.Text.Json;
using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;

namespace HilmaAgent.Infrastructure.Assessments;

/// <summary>
/// The prompt and the response contract, shared by every narrator.
/// </summary>
/// <remarks>
/// Swapping providers has to change <em>who writes the prose</em> and nothing else. If each
/// implementation carried its own prompt, a model comparison would be measuring two prompts as much
/// as two models, and the rule that forbids the model from touching the score would have to be
/// restated — and could be forgotten — in every new provider.
/// </remarks>
public static class NarrationPrompt
{
    public const string System =
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

        Vastaa pelkkänä JSON-oliona, jossa on kentät: recommendation (GO, NO_GO tai INVESTIGATE),
        reasoning (merkkijono) ja citedChunkIds (taulukko annettuja tunnisteita).
        """;

    public static string BuildUser(
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
            foreach (var warning in breakdown.Warnings) builder.AppendLine($"- {warning.Text}");
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

    /// <summary>
    /// Parses the model's JSON reply and validates its citations against what it was actually given.
    /// </summary>
    /// <remarks>
    /// A model that invents an identifier gets it dropped rather than stored — an unverifiable
    /// citation is worse than none, because it looks like evidence. The service checks this again
    /// afterwards; doing it here as well means a new provider cannot forget to.
    /// </remarks>
    public static Narration Parse(string json, IReadOnlyList<NarrationSource> sources)
    {
        using var parsed = JsonDocument.Parse(ExtractJson(json));
        var root = parsed.RootElement;

        var recommendation = root.TryGetProperty("recommendation", out var r) ? r.GetString() : null;
        if (!Recommendation.IsValid(recommendation))
            throw new InvalidOperationException($"Model returned an invalid recommendation: '{recommendation}'.");

        var reasoning = root.TryGetProperty("reasoning", out var reason) ? reason.GetString()?.Trim() : null;
        if (string.IsNullOrWhiteSpace(reasoning))
            throw new InvalidOperationException("Model returned no reasoning.");

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

    /// <summary>
    /// Pulls the JSON object out of a reply that may be wrapped in a fenced code block.
    /// </summary>
    /// <remarks>
    /// Gemini honours a response schema and returns bare JSON. Smaller local models often do not,
    /// and answer with ```json … ``` around it. Being tolerant here is what lets a model running on
    /// your own machine work at all, and it costs nothing when the provider behaves.
    /// </remarks>
    private static string ExtractJson(string reply)
    {
        var text = reply.Trim();

        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text[(firstNewline + 1)..];
            var fence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (fence >= 0) text = text[..fence];
            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');

        return start >= 0 && end > start ? text[start..(end + 1)] : text;
    }

    public static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max] + "…";
}
