using System.Globalization;
using System.Text.Json;
using HilmaAgent.Core.Notices;

namespace HilmaAgent.Infrastructure.Hilma;

/// <summary>
/// Maps the Hilma AVP Read API's <c>NoticeContract</c> onto <see cref="Notice"/>.
/// Field paths are taken from the published OpenAPI document, not guessed.
/// </summary>
/// <remarks>
/// One contract covers legacy F-form, national, and eForms notices — the <c>type</c> discriminator
/// spans all three. That is why there is a single parser rather than one per format.
/// Fields stay nullable throughout: real notices routinely omit values, and an absent value must
/// stay distinguishable from a zero or an empty string for Phase 3 scoring to be honest.
/// </remarks>
public class NoticeContractParser : INoticeParser
{
    public Notice Parse(HilmaNoticeDocument document)
    {
        using var json = JsonDocument.Parse(document.RawJson);
        var root = json.RootElement;

        var project = Get(root, "project");
        var organisation = Get(project, "organisation");
        var organisationInfo = Get(organisation, "information");
        var procurementObject = Get(root, "procurementObject");
        var estimatedValue = Get(procurementObject, "estimatedValue");

        var notice = new Notice
        {
            // The API types noticeId as int32; we key on the string form so the column survives any
            // future identifier scheme without a migration.
            Id = Str(root, "id") ?? document.NoticeId,
            NoticeTypeCode = Int(root, "type"),
            Title = Str(project, "title"),
            BuyerName = Str(organisationInfo, "officialName") ?? FirstOf(Arr(organisation, "officialName")),
            BuyerNationalRegistrationNumber = Str(organisationInfo, "nationalRegistrationNumber"),
            CpvCodes = CollectCpvCodes(root, procurementObject),
            Currency = Str(estimatedValue, "currency"),
            PublicationDate = Date(root, "datePublished"),
            SubmissionDeadline = Date(Get(root, "tenderingInformation"), "tendersOrRequestsToParticipateDueDateTime"),
            Region = CollectRegions(root, organisationInfo),
            Language = Str(root, "language"),
            IsLatest = Bool(root, "isLatest"),
            IsCancelled = Bool(root, "isCancelled"),
            Source = NoticeSource.Legacy,
            RawPayload = document.RawJson,
            FetchedAt = document.FetchedAt,
        };

        notice.NoticeType = HilmaEnums.Name(HilmaEnums.NoticeType, notice.NoticeTypeCode);
        notice.BuyerOrganizationType =
            HilmaEnums.Name(HilmaEnums.ContractingAuthorityType, Int(organisation, "contractingAuthorityType"));

        ApplyEstimatedValue(notice, estimatedValue);
        return notice;
    }

    /// <summary>
    /// ValueRangeContract is either an exact figure or a min/max band (type: Undefined | Exact | Range).
    /// Both are kept — a band collapsed to a single number would silently distort value-fit scoring.
    /// </summary>
    private static void ApplyEstimatedValue(Notice notice, JsonElement? estimatedValue)
    {
        if (estimatedValue is null) return;

        notice.EstimatedValue = Dec(estimatedValue, "value");
        notice.EstimatedValueMin = Dec(estimatedValue, "minValue");
        notice.EstimatedValueMax = Dec(estimatedValue, "maxValue");

        // The buyer can withhold the figure; that is a different fact from "not provided".
        notice.EstimatedValueWithheld = Bool(estimatedValue, "disagreeToBePublished") ?? false;
    }

    /// <summary>
    /// CPV codes live in three places: the notice-level main code, and per-lot main and additional
    /// codes. Phase 3 matching needs the union — a lot-level code is still a code the buyer wants.
    /// </summary>
    private static List<string> CollectCpvCodes(JsonElement root, JsonElement? procurementObject)
    {
        var codes = new List<string?> { Str(Get(procurementObject, "mainCpvCode"), "code") };

        foreach (var lot in Items(root, "objectDescriptions"))
        {
            codes.Add(Str(Get(lot, "mainCpvCode"), "code"));
            foreach (var additional in Items(lot, "additionalCpvCodes"))
                codes.Add(Str(additional, "code"));
        }

        return Distinct(codes);
    }

    /// <summary>NUTS codes come per lot, falling back to the buyer's own region when no lot declares one.</summary>
    private static List<string> CollectRegions(JsonElement root, JsonElement? organisationInfo)
    {
        var regions = new List<string?>();
        foreach (var lot in Items(root, "objectDescriptions"))
            regions.AddRange(Arr(lot, "nutsCodes"));

        if (regions.Count == 0)
            regions.AddRange(Arr(organisationInfo, "nutsCodes"));

        return Distinct(regions);
    }

    private static JsonElement? Get(JsonElement? element, string name) =>
        element is { ValueKind: JsonValueKind.Object } parent
        && parent.TryGetProperty(name, out var child)
        && child.ValueKind is not JsonValueKind.Null
            ? child
            : null;

    private static IEnumerable<JsonElement> Items(JsonElement? element, string name) =>
        Get(element, name) is { ValueKind: JsonValueKind.Array } array
            ? array.EnumerateArray()
            : [];

    private static IEnumerable<string?> Arr(JsonElement? element, string name) =>
        Items(element, name)
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString());

    private static string? Str(JsonElement? element, string name) => Get(element, name) switch
    {
        { ValueKind: JsonValueKind.String } value => Blank(value.GetString()),
        { ValueKind: JsonValueKind.Number } value => value.GetRawText(),
        _ => null,
    };

    private static int? Int(JsonElement? element, string name) =>
        Get(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var parsed)
            ? parsed
            : null;

    private static decimal? Dec(JsonElement? element, string name) =>
        Get(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetDecimal(out var parsed)
            ? parsed
            : null;

    private static bool? Bool(JsonElement? element, string name) => Get(element, name)?.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    /// <summary>
    /// Hilma writes timestamps without a timezone offset, so they are pinned to UTC explicitly.
    /// </summary>
    /// <remarks>
    /// Without <see cref="DateTimeStyles.AssumeUniversal"/> an offsetless timestamp is read as
    /// <em>local</em> time, which makes a stored deadline a property of the machine that fetched the
    /// notice — UTC in the container, UTC+3 on a Finnish developer's machine. That breaks
    /// replayability and moves the deadline gate and its headroom points. Matches
    /// <see cref="JsonPick.Date"/>, which already did this correctly.
    /// </remarks>
    private static DateTimeOffset? Date(JsonElement? element, string name) =>
        Get(element, name) is { ValueKind: JsonValueKind.String } value
        && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime()
            : null;

    private static string? FirstOf(IEnumerable<string?> values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static List<string> Distinct(IEnumerable<string?> values) => values
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(value => value!.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();
}
