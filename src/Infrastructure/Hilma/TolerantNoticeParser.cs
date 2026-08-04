using System.Text.Json;
using HilmaAgent.Core.Notices;

namespace HilmaAgent.Infrastructure.Hilma;

/// <summary>
/// Best-effort mapping from a raw detail document onto <see cref="Notice"/>.
/// Candidate field names cover both the legacy and eForms vocabularies; unrecognised payloads
/// still persist with the raw JSON intact, so nothing is lost while the format decision is open.
/// </summary>
public class TolerantNoticeParser : INoticeParser
{
    private static readonly string[] NoticeTypeNames = ["noticeType", "notice_type", "type", "formType", "noticeSubType"];
    private static readonly string[] TitleNames = ["title", "name", "contractTitle", "shortDescription"];
    private static readonly string[] BuyerNameNames = ["buyerName", "organisationName", "organizationName", "contractingAuthorityName", "officialName", "buyer"];
    private static readonly string[] BuyerTypeNames = ["buyerOrganizationType", "organisationType", "contractingAuthorityType", "buyerLegalType"];
    private static readonly string[] CpvNames = ["cpvCodes", "cpv_codes", "cpvCode", "cpv", "mainCpvCode", "additionalCpvCodes"];
    private static readonly string[] ValueNames = ["estimatedValue", "estimated_value", "estimatedValueExcludingVat", "value", "totalValue", "amount"];
    private static readonly string[] CurrencyNames = ["currency", "currencyCode", "currencyId"];
    private static readonly string[] PublicationDateNames = ["publicationDate", "publication_date", "datePublished", "published", "publishedAt", "dispatchDate"];
    private static readonly string[] DeadlineNames = ["submissionDeadline", "submission_deadline", "deadline", "tenderSubmissionDeadline", "receiptDeadline", "dateReceipt"];
    private static readonly string[] RegionNames = ["region", "nutsCode", "nuts", "mainRegion", "place", "town"];

    public Notice Parse(HilmaNoticeDocument document)
    {
        using var json = JsonDocument.Parse(document.RawJson);
        var root = json.RootElement;

        return new Notice
        {
            Id = document.NoticeId,
            NoticeType = JsonPick.String(root, NoticeTypeNames),
            Title = JsonPick.String(root, TitleNames),
            BuyerName = JsonPick.String(root, BuyerNameNames),
            BuyerOrganizationType = JsonPick.String(root, BuyerTypeNames),
            CpvCodes = JsonPick.Strings(root, CpvNames),
            EstimatedValue = JsonPick.Decimal(root, ValueNames),
            Currency = JsonPick.String(root, CurrencyNames),
            PublicationDate = JsonPick.Date(root, PublicationDateNames),
            SubmissionDeadline = JsonPick.Date(root, DeadlineNames),
            Region = JsonPick.String(root, RegionNames),
            RawPayload = document.RawJson,
            FetchedAt = document.FetchedAt,
        };
    }
}
