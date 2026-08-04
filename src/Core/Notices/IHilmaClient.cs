namespace HilmaAgent.Core.Notices;

/// <summary>
/// A hit from the Hilma search endpoint. The search endpoint returns identifiers only —
/// full content has to be fetched one notice at a time.
/// </summary>
/// <param name="NoticeId">Identifier to pass to the detail endpoint.</param>
/// <param name="PublicationDate">Used to advance the ingestion checkpoint. Null if the search payload does not carry it.</param>
public readonly record struct HilmaNoticeRef(string NoticeId, DateTimeOffset? PublicationDate);

/// <summary>Raw detail response for a single notice. Nothing is parsed at this layer.</summary>
/// <param name="NoticeId">Identifier the document was fetched with.</param>
/// <param name="RawJson">Response body verbatim.</param>
/// <param name="FetchedAt">When we retrieved it.</param>
public sealed record HilmaNoticeDocument(string NoticeId, string RawJson, DateTimeOffset FetchedAt);

public interface IHilmaClient
{
    /// <summary>
    /// Enumerates notice identifiers published on or after <paramref name="publishedAfter"/>,
    /// paging through the search endpoint as the caller consumes results.
    /// </summary>
    IAsyncEnumerable<HilmaNoticeRef> SearchAsync(DateTimeOffset publishedAfter, CancellationToken ct = default);

    /// <summary>Fetches one notice. Returns null when the API reports it as missing (404).</summary>
    Task<HilmaNoticeDocument?> GetNoticeAsync(string noticeId, CancellationToken ct = default);
}
