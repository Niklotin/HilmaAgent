namespace HilmaAgent.Core.Notices;

/// <summary>
/// A procurement notice as we store it. Parsed fields are best-effort; <see cref="RawPayload"/>
/// is always the untouched API response so parsing rules can change without re-fetching.
/// </summary>
public class Notice
{
    /// <summary>Hilma notice identifier (the id used against the detail endpoint).</summary>
    public required string Id { get; set; }

    public string? NoticeType { get; set; }
    public string? Title { get; set; }
    public string? BuyerName { get; set; }
    public string? BuyerOrganizationType { get; set; }
    public List<string> CpvCodes { get; set; } = [];

    /// <summary>Often missing in practice — do not treat null as zero.</summary>
    public decimal? EstimatedValue { get; set; }
    public string? Currency { get; set; }

    public DateTimeOffset? PublicationDate { get; set; }
    public DateTimeOffset? SubmissionDeadline { get; set; }
    public string? Region { get; set; }

    /// <summary>Untouched API response, stored as jsonb.</summary>
    public required string RawPayload { get; set; }

    public DateTimeOffset FetchedAt { get; set; }
}
