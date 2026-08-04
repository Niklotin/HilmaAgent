using HilmaAgent.Core.Notices;

namespace HilmaAgent.Core.Retrieval;

/// <param name="ChunkId">Qdrant point id, which is also <see cref="NoticeChunk.Id"/>.</param>
/// <param name="NoticeId">Owning notice, so a hit can be resolved to a screening candidate.</param>
/// <param name="Score">Cosine similarity. Comparable within a result set, not across models.</param>
public sealed record VectorHit(Guid ChunkId, string NoticeId, string Section, string? LotId, float Score);

/// <summary>Filters applied inside the vector store rather than after retrieval, so top-k stays meaningful.</summary>
/// <param name="CpvPrefixes">Match notices whose CPV codes start with any of these, e.g. "72" for IT services.</param>
/// <param name="NutsPrefixes">Match notices in these NUTS regions, e.g. "FI1B" for Helsinki-Uusimaa.</param>
/// <param name="OpenOnly">Exclude notices whose submission deadline has passed.</param>
public sealed record VectorSearchFilter(
    IReadOnlyList<string>? CpvPrefixes = null,
    IReadOnlyList<string>? NutsPrefixes = null,
    bool OpenOnly = false);

public interface IVectorStore
{
    /// <summary>Creates the collection if absent. Safe to call on every startup.</summary>
    Task EnsureCollectionAsync(int dimensions, CancellationToken ct = default);

    /// <summary>Inserts or replaces points. Re-embedding a notice overwrites rather than duplicates.</summary>
    Task UpsertAsync(IReadOnlyList<(NoticeChunk Chunk, float[] Vector)> points, CancellationToken ct = default);

    Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] query,
        int limit,
        VectorSearchFilter? filter = null,
        CancellationToken ct = default);

    /// <summary>Removes every point belonging to a notice, for re-chunking or deletion.</summary>
    Task DeleteByNoticeAsync(string noticeId, CancellationToken ct = default);

    Task<long> CountAsync(CancellationToken ct = default);
}
