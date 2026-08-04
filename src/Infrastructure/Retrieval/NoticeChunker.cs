using System.Globalization;
using System.Text;
using System.Text.Json;
using HilmaAgent.Core.Notices;

namespace HilmaAgent.Infrastructure.Retrieval;

/// <summary>
/// Splits a notice into embeddable chunks along its own structural boundaries.
/// </summary>
/// <remarks>
/// <para><b>Why section-level rather than whole-notice.</b> A single notice routinely bundles several
/// unrelated lots — road resurfacing in one, IT consultancy in another. Embedding the whole notice
/// averages those into a vector that matches neither well, and a bidder only cares whether *a lot*
/// fits them. So: one chunk for the screening summary, one for the notice-level description, one per
/// lot.</para>
/// <para><b>Why not fixed-size windows.</b> A sliding window would cut across the lot boundary, which
/// is the boundary that carries the meaning. Structure is available here for free — using it beats
/// inferring it back from token offsets.</para>
/// <para><b>Why Finnish labels.</b> The corpus is Finnish and queries will be Finnish; labelling the
/// summary chunk in Finnish keeps the embedded text in one language rather than straddling two.</para>
/// </remarks>
public class NoticeChunker
{
    /// <summary>
    /// Soft cap before a long description is split further. Well under the embedding model's token
    /// limit — the cap exists so one rambling description can't dominate a notice's representation.
    /// </summary>
    public const int MaxChunkCharacters = 3500;

    public IReadOnlyList<NoticeChunk> Chunk(Notice notice)
    {
        var chunks = new List<NoticeChunk>();
        using var payload = JsonDocument.Parse(notice.RawPayload);
        var root = payload.RootElement;

        Add(chunks, notice, ChunkSection.Summary, null, BuildSummary(notice));

        foreach (var description in NoticeDescriptions(notice, root))
            Add(chunks, notice, ChunkSection.Description, null, description);

        foreach (var (lotId, text) in Lots(notice, root))
            Add(chunks, notice, ChunkSection.Lot, lotId, text);

        for (var index = 0; index < chunks.Count; index++)
            chunks[index].ChunkIndex = index;

        return chunks;
    }

    /// <summary>
    /// The facts a bidder screens on, as one self-contained block. Deliberately duplicates structured
    /// columns: a semantic hit has to be readable on its own as a citation, without a second lookup.
    /// </summary>
    private static string BuildSummary(Notice notice)
    {
        var builder = new StringBuilder();
        Append(builder, "Hankinta", notice.Title);
        Append(builder, "Hankintayksikkö", notice.BuyerName);
        Append(builder, "Ilmoituksen tyyppi", notice.NoticeType);
        Append(builder, "CPV-koodit", string.Join(", ", notice.CpvCodes));
        Append(builder, "Alue", string.Join(", ", notice.Region));

        if (notice.EstimatedValue is { } value)
            Append(builder, "Arvioitu arvo", $"{value.ToString("N0", CultureInfo.InvariantCulture)} {notice.Currency}".Trim());
        else if (notice.EstimatedValueMin is { } min && notice.EstimatedValueMax is { } max)
            Append(builder, "Arvioitu arvo", $"{min:N0}–{max:N0} {notice.Currency}".Trim());

        if (notice.SubmissionDeadline is { } deadline)
            Append(builder, "Määräaika", deadline.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return builder.ToString().TrimEnd();
    }

    /// <summary>Notice-level description text, from whichever family's payload this is.</summary>
    private static IEnumerable<string> NoticeDescriptions(Notice notice, JsonElement root)
    {
        var text = notice.Source == NoticeSource.EForms
            // Search index: flat, language-suffixed.
            ? FirstLanguage(root, "description")
            // Legacy contract: an array of paragraph strings.
            : JoinStrings(Property(Property(root, "procurementObject"), "shortDescription"));

        return Split(text);
    }

    private static IEnumerable<(string? LotId, string Text)> Lots(Notice notice, JsonElement root) =>
        notice.Source == NoticeSource.EForms ? EFormsLots(root) : LegacyLots(root);

    private static IEnumerable<(string? LotId, string Text)> EFormsLots(JsonElement root)
    {
        foreach (var lot in Items(root, "lots"))
        {
            var builder = new StringBuilder();
            var lotId = Str(lot, "id");

            Append(builder, "Osa-alue", FirstLanguage(lot, "title") ?? lotId);
            Append(builder, "CPV-koodit", Str(lot, "cpvCodes"));
            Append(builder, "Alue", Str(lot, "nutsCodes"));
            AppendBody(builder, FirstLanguage(lot, "description"));

            foreach (var part in Split(builder.ToString()))
                yield return (lotId, part);
        }
    }

    private static IEnumerable<(string? LotId, string Text)> LegacyLots(JsonElement root)
    {
        foreach (var lot in Items(root, "objectDescriptions"))
        {
            var builder = new StringBuilder();
            var lotId = Str(lot, "lotNumber");

            Append(builder, "Osa-alue", Str(lot, "title") ?? lotId);
            Append(builder, "CPV-koodit", string.Join(", ", CpvCodes(lot)));
            Append(builder, "Alue", JoinStrings(Property(lot, "nutsCodes"), ", "));
            AppendBody(builder, JoinStrings(Property(lot, "descrProcurement")));

            foreach (var part in Split(builder.ToString()))
                yield return (lotId, part);
        }
    }

    private static IEnumerable<string> CpvCodes(JsonElement lot)
    {
        var main = Str(Property(lot, "mainCpvCode"), "code");
        if (main is not null) yield return main;

        foreach (var additional in Items(lot, "additionalCpvCodes"))
            if (Str(additional, "code") is { } code) yield return code;
    }

    /// <summary>
    /// Splits over-long text on paragraph boundaries, falling back to sentence boundaries. Never mid-word:
    /// a chunk that starts mid-sentence embeds badly and reads worse as a citation.
    /// </summary>
    private static IEnumerable<string> Split(string? text)
    {
        text = text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) yield break;

        if (text.Length <= MaxChunkCharacters)
        {
            yield return text;
            yield break;
        }

        var builder = new StringBuilder();
        foreach (var paragraph in SplitUnits(text))
        {
            if (builder.Length > 0 && builder.Length + paragraph.Length > MaxChunkCharacters)
            {
                yield return builder.ToString().Trim();
                builder.Clear();
            }

            builder.Append(paragraph).Append(' ');
        }

        if (builder.Length > 0) yield return builder.ToString().Trim();
    }

    private static IEnumerable<string> SplitUnits(string text)
    {
        var paragraphs = text.Split(["\r\n\r\n", "\n\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var paragraph in paragraphs)
        {
            if (paragraph.Length <= MaxChunkCharacters)
            {
                yield return paragraph;
                continue;
            }

            // A single paragraph over the cap: fall back to sentences.
            var start = 0;
            for (var i = 0; i < paragraph.Length; i++)
            {
                var atBoundary = paragraph[i] is '.' or '!' or '?' or '\n';
                if (!atBoundary && i - start < MaxChunkCharacters) continue;
                if (i - start < MaxChunkCharacters / 4 && atBoundary) continue;

                yield return paragraph[start..(i + 1)].Trim();
                start = i + 1;
            }

            if (start < paragraph.Length) yield return paragraph[start..].Trim();
        }
    }

    private static void Add(List<NoticeChunk> chunks, Notice notice, string section, string? lotId, string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return;

        var id = Guid.NewGuid();
        chunks.Add(new NoticeChunk
        {
            Id = id,
            VectorId = id,
            NoticeId = notice.Id,
            Section = section,
            LotId = lotId,
            Content = content.Trim(),
        });
    }

    private static void Append(StringBuilder builder, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) builder.Append(label).Append(": ").Append(value.Trim()).Append('\n');
    }

    private static void AppendBody(StringBuilder builder, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) builder.Append('\n').Append(value.Trim());
    }

    private static string? FirstLanguage(JsonElement element, string prefix) =>
        Str(element, $"{prefix}Fi") ?? Str(element, $"{prefix}Sv") ?? Str(element, $"{prefix}En") ?? Str(element, $"{prefix}Other");

    private static JsonElement Property(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : default;

    private static IEnumerable<JsonElement> Items(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.Array } array ? array.EnumerateArray() : [];

    private static string? Str(JsonElement element, string name) =>
        Property(element, name) is { ValueKind: JsonValueKind.String } value && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static string? JoinStrings(JsonElement element, string separator = "\n\n") =>
        element.ValueKind == JsonValueKind.Array
            ? string.Join(separator, element.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString()!.Trim())
                .Where(value => value.Length > 0))
            : null;
}
