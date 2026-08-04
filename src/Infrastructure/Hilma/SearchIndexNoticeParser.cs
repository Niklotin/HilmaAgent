using System.Globalization;
using System.Text.Json;
using HilmaAgent.Core.Notices;

namespace HilmaAgent.Infrastructure.Hilma;

/// <summary>
/// Builds a <see cref="Notice"/> from a search index document (index <c>eformnotices-v2</c>).
/// </summary>
/// <remarks>
/// Used for eForms notices. Their detail endpoint returns UBL XML whose structure varies by eForms
/// SDK version, and extracting BT-coded fields from it is a project in itself — while the search
/// index already exposes the same fields the legacy contract does, flattened and typed. So the
/// structured columns come from here and the XML is kept whole on <see cref="Notice.EFormsXml"/>,
/// which is what Phase 2 will chunk. Anything the index omits can be recovered from the XML later
/// without re-fetching.
/// </remarks>
public class SearchIndexNoticeParser
{
    public Notice Parse(HilmaNoticeRef reference)
    {
        using var json = JsonDocument.Parse(reference.Document);
        var root = json.RootElement;

        return new Notice
        {
            Id = reference.SearchId,
            NoticeTypeCode = int.TryParse(Str(root, "type"), out var code) ? code : null,
            // eForms type codes are not the legacy NoticeType enum: "16" is a contract notice here,
            // "E3" a national one. mainType is the index's own grouping and is the readable form.
            NoticeType = Str(root, "mainType"),
            Title = FirstLanguage(root, "title"),
            BuyerName = FirstLanguage(root, "organisationName"),
            BuyerNationalRegistrationNumber = Str(root, "organisationNationalRegistrationNumber"),
            BuyerOrganizationType = Str(root, "organisationType"),
            CpvCodes = SpaceSeparated(root, "cpvCodes"),
            Region = SpaceSeparated(root, "nutsCodes", "organisationNutsCode"),
            EstimatedValue = Dec(root, "estimatedValue"),
            Currency = Str(root, "currency"),
            PublicationDate = reference.PublicationDate ?? Date(root, "datePublished"),
            SubmissionDeadline = Date(root, "deadline"),
            Language = null,
            IsLatest = null,
            IsCancelled = Bool(root, "isCancelled"),
            Source = NoticeSource.EForms,
            RawPayload = reference.Document,
            FetchedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Titles and buyer names are split per language (…Fi/Sv/En/Other). Finnish first: these are
    /// Finnish notices, and the other columns are frequently empty.
    /// </summary>
    private static string? FirstLanguage(JsonElement root, string prefix) =>
        Str(root, $"{prefix}Fi") ?? Str(root, $"{prefix}Sv") ?? Str(root, $"{prefix}En") ?? Str(root, $"{prefix}Other");

    /// <summary>The index packs multi-value fields into one space-separated string, e.g. "38000000 42661100".</summary>
    private static List<string> SpaceSeparated(JsonElement root, params string[] names) => names
        .Select(name => Str(root, name))
        .Where(value => value is not null)
        .SelectMany(value => value!.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

    private static JsonElement? Get(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is not JsonValueKind.Null ? value : null;

    private static string? Str(JsonElement root, string name) => Get(root, name) switch
    {
        { ValueKind: JsonValueKind.String } value => string.IsNullOrWhiteSpace(value.GetString()) ? null : value.GetString()!.Trim(),
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => null,
    };

    private static decimal? Dec(JsonElement root, string name) =>
        Get(root, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDecimal(out var parsed) ? parsed : null;

    private static bool? Bool(JsonElement root, string name) => Get(root, name)?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static DateTimeOffset? Date(JsonElement root, string name) =>
        Get(root, name) is { ValueKind: JsonValueKind.String } value
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
}
