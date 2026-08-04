using HilmaAgent.Core.Ingestion;
using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HilmaAgent.Infrastructure.Ingestion;

public record IngestionResult(int Stored, int StoredEForms, int Skipped, int Failed, int MissingContent);

/// <summary>
/// One incremental ingestion pass: search for notices published since the checkpoint, then fill in
/// content by family — legacy notices one detail call each, eForms notices in batches of up to 50.
/// The checkpoint only advances past notices that were actually stored, so a crash mid-run
/// re-fetches rather than skips.
/// </summary>
public class NoticeIngestionService(
    IHilmaClient client,
    INoticeParser parser,
    SearchIndexNoticeParser searchParser,
    HilmaDbContext db,
    IOptions<HilmaOptions> options,
    ILogger<NoticeIngestionService> logger)
{
    /// <summary>How far back to look on a first ever run.</summary>
    public static readonly TimeSpan InitialLookback = TimeSpan.FromDays(30);

    private readonly HilmaOptions _options = options.Value;

    public async Task<IngestionResult> RunAsync(int maxNotices, CancellationToken ct = default)
    {
        var checkpoint = await db.IngestionCheckpoints
            .FirstOrDefaultAsync(c => c.Source == IngestionCheckpoint.HilmaSource, ct);

        var since = checkpoint?.LastSeenPublicationDate ?? DateTimeOffset.UtcNow - InitialLookback;
        logger.LogInformation("Ingestion pass starting from {Since:O} (max {MaxNotices} notices).", since, maxNotices);

        int stored = 0, storedEForms = 0, skipped = 0, failed = 0, missingContent = 0;
        var highWaterMark = since;
        var eFormsBuffer = new List<HilmaNoticeRef>(_options.EFormsBatchSize);
        var considered = 0;

        await foreach (var reference in client.SearchAsync(since, ct))
        {
            if (considered >= maxNotices) break;
            ct.ThrowIfCancellationRequested();

            // Key on the search id (EF-52994 / OLD-52994), never the bare noticeId: the two id spaces
            // overlap, and the legacy Read API resolves a shared number to the legacy notice.
            if (await db.Notices.AnyAsync(n => n.Id == reference.SearchId, ct))
            {
                skipped++;
                continue;
            }

            considered++;

            if (reference.IsEForms)
            {
                eFormsBuffer.Add(reference);
                if (eFormsBuffer.Count >= _options.EFormsBatchSize)
                    (storedEForms, missingContent, failed, highWaterMark) =
                        await FlushEFormsAsync(eFormsBuffer, storedEForms, missingContent, failed, highWaterMark, ct);
                continue;
            }

            try
            {
                var document = await client.GetNoticeAsync(reference.NoticeId.ToString(), ct);
                if (document is null)
                {
                    missingContent++;
                    continue;
                }

                var notice = parser.Parse(document);
                notice.Id = reference.SearchId;
                notice.Source = NoticeSource.Legacy;

                db.Notices.Add(notice);
                await db.SaveChangesAsync(ct);
                stored++;
                highWaterMark = Advance(highWaterMark, notice.PublicationDate ?? reference.PublicationDate);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // One bad notice must not abort the pass; it will be retried next run because the
                // checkpoint never moves past a notice we failed to store.
                logger.LogError(ex, "Failed to ingest notice {SearchId}.", reference.SearchId);
                failed++;
            }
        }

        if (eFormsBuffer.Count > 0)
            (storedEForms, missingContent, failed, highWaterMark) =
                await FlushEFormsAsync(eFormsBuffer, storedEForms, missingContent, failed, highWaterMark, ct);

        if (stored + storedEForms > 0 && failed == 0 && highWaterMark > since)
            await AdvanceCheckpointAsync(checkpoint, highWaterMark, ct);

        var result = new IngestionResult(stored, storedEForms, skipped, failed, missingContent);
        logger.LogInformation(
            "Ingestion pass complete: {Stored} legacy + {StoredEForms} eForms stored, {Skipped} already known, {Failed} failed, {MissingContent} without content.",
            result.Stored, result.StoredEForms, result.Skipped, result.Failed, result.MissingContent);

        return result;
    }

    /// <summary>
    /// Stores one batch of eForms notices. Structured fields come from the search document already in
    /// hand; the batch call supplies the XML body, 50 notices per request.
    /// </summary>
    private async Task<(int Stored, int MissingContent, int Failed, DateTimeOffset HighWaterMark)> FlushEFormsAsync(
        List<HilmaNoticeRef> buffer,
        int stored,
        int missingContent,
        int failed,
        DateTimeOffset highWaterMark,
        CancellationToken ct)
    {
        try
        {
            var ids = buffer.Select(reference => reference.NoticeId).Where(id => id > 0).ToList();
            var documents = (await client.GetEFormsNoticesAsync(ids, ct)).ToDictionary(d => d.NoticeId);

            foreach (var reference in buffer)
            {
                var notice = searchParser.Parse(reference);

                if (documents.TryGetValue(reference.NoticeId, out var document))
                {
                    notice.EFormsXml = document.Xml;
                }
                else
                {
                    // Stored anyway: the index fields are still a usable screening record, and the
                    // gap is visible because EFormsXml is null rather than silently empty.
                    logger.LogWarning("No eForms content returned for {SearchId}; storing index fields only.", reference.SearchId);
                    missingContent++;
                }

                db.Notices.Add(notice);
                highWaterMark = Advance(highWaterMark, notice.PublicationDate);
                stored++;
            }

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to ingest a batch of {Count} eForms notices.", buffer.Count);
            failed += buffer.Count;
            // Drop the tracked-but-unsaved entities so the next batch is not poisoned by them.
            foreach (var entry in db.ChangeTracker.Entries<Notice>().ToList()) entry.State = EntityState.Detached;
        }
        finally
        {
            buffer.Clear();
        }

        return (stored, missingContent, failed, highWaterMark);
    }

    private static DateTimeOffset Advance(DateTimeOffset current, DateTimeOffset? candidate) =>
        candidate is { } date && date > current ? date : current;

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
