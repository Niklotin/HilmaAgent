using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HilmaAgent.Api.Notices;

/// <summary>Read-only views over ingested notices. Enough to verify Phase 1 without a frontend.</summary>
public static class NoticeEndpoints
{
    public static IEndpointRouteBuilder MapNoticeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/notices").WithTags("Notices");

        group.MapGet("/", async (
            HilmaDbContext db,
            CancellationToken ct,
            string? cpv = null,
            bool openOnly = false,
            int skip = 0,
            int take = 50) =>
        {
            take = Math.Clamp(take, 1, 200);

            var query = db.Notices.AsNoTracking();

            if (!string.IsNullOrWhiteSpace(cpv))
                query = query.Where(n => n.CpvCodes.Contains(cpv));

            if (openOnly)
                query = query.Where(n => n.SubmissionDeadline != null && n.SubmissionDeadline > DateTimeOffset.UtcNow);

            var total = await query.CountAsync(ct);
            var items = await query
                .OrderByDescending(n => n.PublicationDate)
                .Skip(skip)
                .Take(take)
                .Select(n => NoticeSummaryResponse.From(n))
                .ToListAsync(ct);

            return Results.Ok(new { total, skip, take, items });
        })
        .WithName("ListNotices")
        .WithSummary("Lists ingested notices, newest first.");

        group.MapGet("/{id}", async (string id, HilmaDbContext db, CancellationToken ct) =>
        {
            var notice = await db.Notices.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, ct);
            return notice is null ? Results.NotFound() : Results.Ok(notice);
        })
        .WithName("GetNotice")
        .WithSummary("Returns a single notice including its raw payload and, for eForms, its XML.");

        group.MapGet("/{id}/eforms.xml", async (string id, HilmaDbContext db, CancellationToken ct) =>
        {
            var xml = await db.Notices.AsNoTracking().Where(n => n.Id == id).Select(n => n.EFormsXml).FirstOrDefaultAsync(ct);
            return xml is null ? Results.NotFound() : Results.Text(xml, "application/xml");
        })
        .WithName("GetNoticeEForms")
        .WithSummary("Raw eForms UBL XML for a notice, when one was stored.");

        group.MapGet("/stats", async (HilmaDbContext db, CancellationToken ct) => Results.Ok(new
        {
            total = await db.Notices.CountAsync(ct),
            bySource = await db.Notices.GroupBy(n => n.Source)
                .Select(g => new { source = g.Key, count = g.Count() }).ToListAsync(ct),
            withDeadline = await db.Notices.CountAsync(n => n.SubmissionDeadline != null, ct),
            withEstimatedValue = await db.Notices.CountAsync(n => n.EstimatedValue != null, ct),
            withEFormsXml = await db.Notices.CountAsync(n => n.EFormsXml != null, ct),
            newestPublication = await db.Notices.MaxAsync(n => (DateTimeOffset?)n.PublicationDate, ct),
            checkpoint = await db.IngestionCheckpoints
                .Select(c => new { c.Source, c.LastSeenPublicationDate, c.UpdatedAt })
                .ToListAsync(ct),
        }))
        .WithName("GetIngestionStats")
        .WithSummary("Ingestion health: how much landed, and how much of it actually parsed.");

        return app;
    }
}

/// <summary>List projection — omits RawPayload, which is large and only useful per notice.</summary>
public record NoticeSummaryResponse(
    string Id,
    string? Title,
    string? BuyerName,
    string? NoticeType,
    List<string> CpvCodes,
    decimal? EstimatedValue,
    decimal? EstimatedValueMin,
    decimal? EstimatedValueMax,
    bool EstimatedValueWithheld,
    string? Currency,
    DateTimeOffset? PublicationDate,
    DateTimeOffset? SubmissionDeadline,
    List<string> Region,
    DateTimeOffset FetchedAt)
{
    public static NoticeSummaryResponse From(Notice n) => new(
        n.Id, n.Title, n.BuyerName, n.NoticeType, n.CpvCodes,
        n.EstimatedValue, n.EstimatedValueMin, n.EstimatedValueMax, n.EstimatedValueWithheld,
        n.Currency, n.PublicationDate, n.SubmissionDeadline, n.Region, n.FetchedAt);
}
