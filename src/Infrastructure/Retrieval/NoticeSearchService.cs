using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Retrieval;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HilmaAgent.Infrastructure.Retrieval;

/// <param name="Score">Best-scoring chunk for this notice. Comparable within a result set only.</param>
/// <param name="Chunks">The matching passages, best first — these become Phase 3's citations.</param>
public sealed record NoticeSearchResult(
    string NoticeId,
    string? Title,
    string? BuyerName,
    List<string> CpvCodes,
    decimal? EstimatedValue,
    string? Currency,
    DateTimeOffset? SubmissionDeadline,
    float Score,
    List<SearchChunk> Chunks);

public sealed record SearchChunk(Guid ChunkId, string Section, string? LotId, float Score, string Content);

/// <summary>
/// Semantic search over the notice index, grouped up from chunk hits to notice hits.
/// </summary>
/// <remarks>
/// Retrieval matches <em>chunks</em>, but a bidder screens <em>notices</em>. Returning raw chunk hits
/// would let one notice with three strong lots crowd out five other notices, so hits are grouped by
/// notice and scored by their best chunk. The contributing chunks travel with the result because
/// Phase 3 has to cite them.
/// </remarks>
public class NoticeSearchService(
    HilmaDbContext db,
    IEmbeddingProvider embeddings,
    IVectorStore vectors)
{
    public async Task<IReadOnlyList<NoticeSearchResult>> SearchAsync(
        string query,
        int limit = 10,
        VectorSearchFilter? filter = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var embedded = await embeddings.EmbedAsync([query], ct);

        // Over-fetch: several chunks of one notice can occupy the top of the chunk ranking, so a
        // limit applied at chunk level would return fewer than `limit` distinct notices.
        var hits = await vectors.SearchAsync(embedded[0], limit * 5, filter, ct);
        if (hits.Count == 0) return [];

        var noticeIds = hits.Select(hit => hit.NoticeId).Distinct().ToList();

        var notices = await db.Notices.AsNoTracking()
            .Where(n => noticeIds.Contains(n.Id))
            .ToDictionaryAsync(n => n.Id, ct);

        var chunkIds = hits.Select(hit => hit.ChunkId).ToList();
        var contents = await db.NoticeChunks.AsNoTracking()
            .Where(c => chunkIds.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, c => c.Content, ct);

        return hits
            .GroupBy(hit => hit.NoticeId)
            .Select(group =>
            {
                var notice = notices.GetValueOrDefault(group.Key);
                var ordered = group.OrderByDescending(hit => hit.Score).ToList();

                return new NoticeSearchResult(
                    group.Key,
                    notice?.Title,
                    notice?.BuyerName,
                    notice?.CpvCodes ?? [],
                    notice?.EstimatedValue,
                    notice?.Currency,
                    notice?.SubmissionDeadline,
                    ordered[0].Score,
                    ordered.Select(hit => new SearchChunk(
                        hit.ChunkId,
                        hit.Section,
                        hit.LotId,
                        hit.Score,
                        contents.GetValueOrDefault(hit.ChunkId, string.Empty))).ToList());
            })
            .OrderByDescending(result => result.Score)
            .Take(limit)
            .ToList();
    }
}
