using System.Text.Json;
using HilmaAgent.Core.Assessments;
using HilmaAgent.Infrastructure.Assessments;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HilmaAgent.Api.Notices;

public static class AssessmentEndpoints
{
    private static readonly JsonSerializerOptions SseJson = new(JsonSerializerDefaults.Web);

    public static IEndpointRouteBuilder MapAssessmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Assessments");

        group.MapPost("/assess/{noticeId}", async (
            string noticeId,
            AssessmentService assessments,
            CancellationToken ct,
            Guid? profileId = null) =>
        {
            try
            {
                return Results.Ok(await assessments.AssessAsync(noticeId, profileId, ct: ct));
            }
            catch (KeyNotFoundException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .Produces<FitAssessment>()
        .WithName("AssessNotice")
        .WithSummary("Scores a notice in code, then has the model justify that score.");

        // SSE: the assessment takes several seconds and does distinct steps. Streaming them shows the
        // deterministic score arriving before the model is called, which is the architecture made visible.
        group.MapGet("/assess/{noticeId}/stream", async (
            string noticeId,
            HttpContext context,
            AssessmentService assessments,
            CancellationToken ct,
            Guid? profileId = null) =>
        {
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            async Task Send(string eventName, object payload)
            {
                await context.Response.WriteAsync($"event: {eventName}\ndata: {JsonSerializer.Serialize(payload, SseJson)}\n\n", ct);
                await context.Response.Body.FlushAsync(ct);
            }

            var queue = new Queue<AssessmentProgress>();
            var progress = new Progress<AssessmentProgress>(queue.Enqueue);

            try
            {
                var task = assessments.AssessAsync(noticeId, profileId, progress, ct);

                while (!task.IsCompleted)
                {
                    while (queue.Count > 0) await Send("progress", queue.Dequeue());
                    await Task.Delay(100, ct);
                }

                while (queue.Count > 0) await Send("progress", queue.Dequeue());
                await Send("assessment", await task);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Client hung up mid-assessment; nothing to report to a closed connection.
            }
            catch (Exception ex)
            {
                await Send("error", new { error = ex.Message });
            }
        })
        .WithName("AssessNoticeStream")
        .WithSummary("Same assessment, streamed step by step over SSE.");

        group.MapGet("/assessments", async (HilmaDbContext db, CancellationToken ct, int take = 20, bool disagreementsOnly = false) =>
        {
            var query = db.FitAssessments.AsNoTracking();
            if (disagreementsOnly) query = query.Where(a => a.RecommendationDisagreement);

            return Results.Ok(await query
                .OrderByDescending(a => a.CreatedAt)
                .Take(Math.Clamp(take, 1, 100))
                .Select(a => new
                {
                    a.Id,
                    a.NoticeId,
                    noticeTitle = a.Notice!.Title,
                    a.DeterministicScore,
                    a.ScoreRecommendation,
                    a.ModelRecommendation,
                    a.RecommendationDisagreement,
                    a.Reasoning,
                    citationCount = a.Citations.Count,
                    a.ModelId,
                    a.CreatedAt,
                })
                .ToListAsync(ct));
        })
        .WithName("ListAssessments")
        .WithSummary("Recent assessments; filter to the cases where the model and the score disagreed.");

        group.MapGet("/assessments/{id:guid}", async (Guid id, HilmaDbContext db, CancellationToken ct) =>
        {
            var assessment = await db.FitAssessments.AsNoTracking()
                .Include(a => a.Notice)
                .FirstOrDefaultAsync(a => a.Id == id, ct);

            return assessment is null ? Results.NotFound() : Results.Ok(assessment);
        })
        .Produces<FitAssessment>()
        .WithName("GetAssessment")
        .WithSummary("One assessment, including its full score breakdown and citations.");

        return app;
    }
}

