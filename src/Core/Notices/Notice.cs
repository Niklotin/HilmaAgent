namespace HilmaAgent.Core.Notices;

/// <summary>
/// A procurement notice as we store it. Parsed fields are best-effort; <see cref="RawPayload"/>
/// is always the untouched API response so parsing rules can change without re-fetching.
/// </summary>
public class Notice
{
    /// <summary>Hilma notice identifier. Numeric in the API; kept as text so the key is scheme-agnostic.</summary>
    public required string Id { get; set; }

    /// <summary>Raw <c>NoticeType</c> enum value from the API.</summary>
    public int? NoticeTypeCode { get; set; }

    /// <summary>Human-readable name for <see cref="NoticeTypeCode"/>, e.g. NationalContract, EForms16.</summary>
    public string? NoticeType { get; set; }

    public string? Title { get; set; }
    public string? BuyerName { get; set; }

    /// <summary>Finnish business ID (y-tunnus) where the buyer supplied one.</summary>
    public string? BuyerNationalRegistrationNumber { get; set; }

    public string? BuyerOrganizationType { get; set; }

    /// <summary>Union of the notice-level main code and every lot's main and additional codes.</summary>
    public List<string> CpvCodes { get; set; } = [];

    /// <summary>Exact estimated value, when the buyer gave one. Frequently absent — null is not zero.</summary>
    public decimal? EstimatedValue { get; set; }

    /// <summary>Lower bound, when the value was expressed as a range rather than a figure.</summary>
    public decimal? EstimatedValueMin { get; set; }

    /// <summary>Upper bound, when the value was expressed as a range rather than a figure.</summary>
    public decimal? EstimatedValueMax { get; set; }

    /// <summary>The buyer explicitly withheld the value — distinct from simply not providing one.</summary>
    public bool EstimatedValueWithheld { get; set; }

    public string? Currency { get; set; }

    public DateTimeOffset? PublicationDate { get; set; }
    public DateTimeOffset? SubmissionDeadline { get; set; }

    /// <summary>NUTS codes, per lot where given, otherwise the buyer's own region.</summary>
    public List<string> Region { get; set; } = [];

    /// <summary>Notice language (FI, SV, EN).</summary>
    public string? Language { get; set; }

    /// <summary>False when a later corrigendum supersedes this notice.</summary>
    public bool? IsLatest { get; set; }

    public bool? IsCancelled { get; set; }

    /// <summary>Untouched API response, stored as jsonb.</summary>
    public required string RawPayload { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
