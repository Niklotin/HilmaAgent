using HilmaAgent.Core.Ingestion;
using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HilmaAgent.Infrastructure.Ingestion;

public record IngestionResult(int Fetched, int Stored, int Skipped, int Failed, int UnavailableEForms);

/// <summary>
/// One incremental ingestion pass: search for notices published since the checkpoint, fetch each
/// one's detail document, parse it, and store it. The checkpoint only advances past notices that
/// were actually stored, so a crash mid-run re-fetches rather than skips.
/// </summary>
public class NoticeIngestionService(
    IHilmaClient client,
    INoticeParser parser,
    HilmaDbContext db,
    ILogger<NoticeIngestionService> logger)
{
    /// <summary>How far back to look on a first ever run.</summary>
    public static readonly TimeSpan InitialLookback = TimeSpan.FromDays(30);

    public async Task<IngestionResult> RunAsync(int maxNotices, CancellationToken ct = default)
    {
        var checkpoint = await db.IngestionCheckpoints
            .FirstOrDefaultAsync(c => c.Source == IngestionCheckpoint.HilmaSource, ct);

        var since = checkpoint?.LastSeenPublicationDate ?? DateTimeOffset.UtcNow - InitialLookback;
        logger.LogInformation("Ingestion pass starting from {Since:O} (max {MaxNotices} notices).", since, maxNotices);

        int fetched = 0, stored = 0, skipped = 0, failed = 0, unavailableEForms = 0;
        var highWaterMark = since;

        await foreach (var reference in client.SearchAsync(since, ct))
        {
            if (fetched >= maxNotices) break;
            ct.ThrowIfCancellationRequested();

            // Key on the search id (EF-52994 / OLD-52994), never the bare noticeId: the two id
            // spaces overlap. noticeId 52994 is a July 2026 eForms notice *and* a September 2020
            // legacy one, and the Read API resolves the number to the legacy notice.
            if (await db.Notices.AnyAsync(n => n.Id == reference.SearchId, ct))
            {
                skipped++;
                continue;
            }

            if (reference.IsEForms)
            {
                // The legacy Read API does not serve eForms notices, and asking it for this
                // noticeId would return whichever unrelated legacy notice shares the number.
                // Requires the separate eForms Read API — see README.
                unavailableEForms++;
                continue;
            }

            fetched++;
            Notice notice;
            try
            {
                var document = await client.GetNoticeAsync(reference.NoticeId.ToString(), ct);
                if (document is null)
                {
                    skipped++;
                    continue;
                }

                notice = parser.Parse(document);
                notice.Id = reference.SearchId;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad notice must not abort the pass; it will be retried next run because the
                // checkpoint never moves past a notice we failed to store.
                logger.LogError(ex, "Failed to ingest notice {SearchId}.", reference.SearchId);
                failed++;
                continue;
            }

            db.Notices.Add(notice);
            await db.SaveChangesAsync(ct);
            stored++;

            var published = notice.PublicationDate ?? reference.PublicationDate;
            if (published is { } date && date > highWaterMark) highWaterMark = date;
        }

        if (stored > 0 && failed == 0 && highWaterMark > since)
            await AdvanceCheckpointAsync(checkpoint, highWaterMark, ct);

        var result = new IngestionResult(fetched, stored, skipped, failed, unavailableEForms);
        logger.LogInformation(
            "Ingestion pass complete: {Stored} stored, {Skipped} skipped, {Failed} failed, {UnavailableEForms} eForms notices unavailable from the Read API.",
            result.Stored, result.Skipped, result.Failed, result.UnavailableEForms);

        return result;
    }

    private async Task AdvanceCheckpointAsync(IngestionCheckpoint? checkpoint, DateTimeOffset to, CancellationToken ct)
    {
        if (checkpoint is null)
        {
            checkpoint = new IngestionCheckpoint { Source = IngestionCheckpoint.HilmaSource };
            db.IngestionCheckpoints.Add(checkpoint);
        }

        checkpoint.LastSeenPublicationDate = to;
        checkpoint.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Ingestion checkpoint advanced to {Checkpoint:O}.", to);
    }
}
