using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Retrieval;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HilmaAgent.Infrastructure.Retrieval;

public record IndexingResult(int NoticesIndexed, int ChunksWritten, int Failed);

/// <summary>
/// Chunks un-indexed notices, embeds the chunks, and writes them to Postgres and Qdrant.
/// Runs after ingestion and is safe to re-run: a notice is re-chunked from scratch, so a chunking
/// change is applied by clearing <c>EmbeddedAt</c> rather than by re-fetching anything.
/// </summary>
public class NoticeIndexingService(
    HilmaDbContext db,
    NoticeChunker chunker,
    IEmbeddingProvider embeddings,
    IVectorStore vectors,
    ILogger<NoticeIndexingService> logger)
{
    public async Task<IndexingResult> RunAsync(int maxNotices, CancellationToken ct = default)
    {
        await vectors.EnsureCollectionAsync(embeddings.Dimensions, ct);

        // A notice needs indexing if it has no chunks, or if its chunks were embedded with a
        // different model than the one configured now.
        var pending = await db.Notices
            .Where(n => !db.NoticeChunks.Any(c => c.NoticeId == n.Id && c.EmbeddingModel == embeddings.ModelId))
            .OrderBy(n => n.PublicationDate)
            .Take(maxNotices)
            .ToListAsync(ct);

        if (pending.Count == 0)
        {
            logger.LogInformation("Indexing pass: nothing pending.");
            return new IndexingResult(0, 0, 0);
        }

        logger.LogInformation("Indexing {Count} notices with {Model}.", pending.Count, embeddings.ModelId);

        int indexed = 0, written = 0, failed = 0;

        foreach (var notice in pending)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var chunks = chunker.Chunk(notice);
                if (chunks.Count == 0)
                {
                    logger.LogWarning("Notice {NoticeId} produced no chunks; nothing to embed.", notice.Id);
                    continue;
                }

                var vectorsForChunks = await embeddings.EmbedAsync(chunks.Select(c => c.Content).ToList(), ct);

                foreach (var chunk in chunks)
                {
                    chunk.Notice = notice;
                    chunk.EmbeddingModel = embeddings.ModelId;
                    chunk.EmbeddedAt = DateTimeOffset.UtcNow;
                }

                // Replace rather than append: re-indexing must not leave the old chunks orphaned in
                // either store, or search starts returning text that no longer exists.
                await vectors.DeleteByNoticeAsync(notice.Id, ct);
                await db.NoticeChunks.Where(c => c.NoticeId == notice.Id).ExecuteDeleteAsync(ct);

                db.NoticeChunks.AddRange(chunks);
                await db.SaveChangesAsync(ct);

                await vectors.UpsertAsync(chunks.Zip(vectorsForChunks).Select(pair => (pair.First, pair.Second)).ToList(), ct);

                indexed++;
                written += chunks.Count;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Failed to index notice {NoticeId}.", notice.Id);
                failed++;
            }
        }

        var result = new IndexingResult(indexed, written, failed);
        logger.LogInformation("Indexing pass complete: {Indexed} notices, {Chunks} chunks, {Failed} failed.",
            result.NoticesIndexed, result.ChunksWritten, result.Failed);

        return result;
    }
}
