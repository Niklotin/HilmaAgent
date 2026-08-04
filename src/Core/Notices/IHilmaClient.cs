namespace HilmaAgent.Core.Notices;

/// <summary>A hit from the Hilma search index.</summary>
/// <param name="SearchId">Index key: <c>EF-52662</c> (eForms), <c>OLD-160524</c> (legacy), <c>PLAN-1263</c> (procurement plan).</param>
/// <param name="NoticeId">Identifier for the Read API detail endpoint. Zero for plans, which have no underlying notice.</param>
/// <param name="IsEForms">eForms notices are not served by the legacy Read API — see the README.</param>
/// <param name="PublicationDate">Drives the ingestion checkpoint.</param>
/// <param name="Document">
/// The raw search hit. Unlike a bare identifier list this already carries title, buyer, CPV, NUTS,
/// value and deadline, so it is worth keeping rather than discarding after the id is read.
/// </param>
public sealed record HilmaNoticeRef(
    string SearchId,
    int NoticeId,
    bool IsEForms,
    DateTimeOffset? PublicationDate,
    string Document);

/// <summary>Raw detail response for a single notice. Nothing is parsed at this layer.</summary>
/// <param name="NoticeId">Identifier the document was fetched with.</param>
/// <param name="RawJson">Response body verbatim.</param>
/// <param name="FetchedAt">When we retrieved it.</param>
public sealed record HilmaNoticeDocument(string NoticeId, string RawJson, DateTimeOffset FetchedAt);

public interface IHilmaClient
{
    /// <summary>
    /// Enumerates search hits published after <paramref name="publishedAfter"/>, oldest first,
    /// paging as the caller consumes results.
    /// </summary>
    /// <remarks>
    /// Ascending order matters: the ingestion checkpoint is a high-water mark, and advancing it
    /// over a partially consumed descending page would skip everything below it.
    /// </remarks>
    IAsyncEnumerable<HilmaNoticeRef> SearchAsync(DateTimeOffset publishedAfter, CancellationToken ct = default);

    /// <summary>Fetches one notice from the legacy Read API. Returns null when the API reports it missing (404).</summary>
    Task<HilmaNoticeDocument?> GetNoticeAsync(string noticeId, CancellationToken ct = default);

    /// <summary>
    /// Fetches eForms notice content in one request. The API accepts up to 50 ids per call, which is
    /// what makes ingesting the eForms majority practical under a rate limit.
    /// </summary>
    Task<IReadOnlyList<HilmaEFormsDocument>> GetEFormsNoticesAsync(
        IReadOnlyCollection<int> noticeIds,
        CancellationToken ct = default);
}

/// <summary>
/// An eForms notice as served by the eForms Read API: a thin JSON envelope around the notice itself,
/// which is base64-encoded UBL XML (see the OP-TED eForms SDK).
/// </summary>
/// <param name="NoticeId">Hilma notice id the content was fetched with.</param>
/// <param name="Xml">Decoded eForms XML. Root element varies by notice kind: ContractNotice, ContractAwardNotice, PriorInformationNotice, …</param>
/// <param name="Envelope">The JSON envelope verbatim, minus the base64 blob's redundancy.</param>
/// <param name="FetchedAt">When we retrieved it.</param>
public sealed record HilmaEFormsDocument(int NoticeId, string Xml, string Envelope, DateTimeOffset FetchedAt);
