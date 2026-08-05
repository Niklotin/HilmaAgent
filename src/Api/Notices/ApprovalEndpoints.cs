using HilmaAgent.Core.Assessments;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HilmaAgent.Api.Notices;

/// <param name="Decision">APPROVED, REJECTED, or EDITED.</param>
/// <param name="EditedRecommendation">Required when EDITED; ignored otherwise.</param>
public record DecisionRequest(string Decision, string? EditedRecommendation, string? ReviewerNote, string? ReviewedBy);

public static class ApprovalEndpoints
{
    public static IEndpointRouteBuilder MapApprovalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Approvals");

        // The queue: assessments nobody has ruled on yet, worst-blocked last so the reviewer's
        // attention goes where a decision is actually needed.
        group.MapGet("/queue", async (HilmaDbContext db, CancellationToken ct, int take = 25, bool disagreementsFirst = true) =>
        {
            var decided = db.ApprovalDecisions.Select(d => d.AssessmentId);

            var query = db.FitAssessments.AsNoTracking()
                .Where(a => !decided.Contains(a.Id));

            query = disagreementsFirst
                ? query.OrderByDescending(a => a.RecommendationDisagreement).ThenByDescending(a => a.DeterministicScore)
                : query.OrderByDescending(a => a.DeterministicScore);

            return Results.Ok(await query
                .Take(Math.Clamp(take, 1, 100))
                .Select(a => new
                {
                    a.Id,
                    a.NoticeId,
                    noticeTitle = a.Notice!.Title,
                    buyerName = a.Notice.BuyerName,
                    a.Notice.SubmissionDeadline,
                    a.Notice.EstimatedValue,
                    a.Notice.Currency,
                    a.DeterministicScore,
                    a.ScoreRecommendation,
                    a.ModelRecommendation,
                    a.RecommendationDisagreement,
                    a.Reasoning,
                    a.Citations,
                    a.ModelId,
                    a.CreatedAt,
                })
                .ToListAsync(ct));
        })
        .WithName("GetApprovalQueue")
        .WithSummary("Assessments awaiting a human decision, disagreements first.");

        group.MapPost("/assessments/{id:guid}/decision", async (
            Guid id,
            DecisionRequest request,
            HilmaDbContext db,
            CancellationToken ct) =>
        {
            if (!DecisionType.IsValid(request.Decision))
                return Results.BadRequest(new { error = $"Decision must be one of APPROVED, REJECTED, EDITED; got '{request.Decision}'." });

            if (request.Decision == DecisionType.Edited && !Recommendation.IsValid(request.EditedRecommendation))
                return Results.BadRequest(new { error = "EDITED requires EditedRecommendation to be GO, NO_GO, or INVESTIGATE." });

            if (!await db.FitAssessments.AnyAsync(a => a.Id == id, ct))
                return Results.NotFound(new { error = $"Assessment '{id}' not found." });

            var decision = new ApprovalDecision
            {
                Id = Guid.NewGuid(),
                AssessmentId = id,
                Decision = request.Decision,
                // Kept only where it means something, so an EDITED filter never picks up stale values.
                EditedRecommendation = request.Decision == DecisionType.Edited ? request.EditedRecommendation : null,
                ReviewerNote = request.ReviewerNote?.Trim(),
                ReviewedBy = request.ReviewedBy?.Trim(),
                DecidedAt = DateTimeOffset.UtcNow,
            };

            // Append-only: a changed mind writes a new row. An audit trail you can quietly revise
            // is not an audit trail.
            db.ApprovalDecisions.Add(decision);
            await db.SaveChangesAsync(ct);

            return Results.Created($"/api/assessments/{id}/decisions", decision);
        })
        .WithName("RecordDecision")
        .WithSummary("Records a human verdict on an assessment. Append-only.");

        group.MapGet("/assessments/{id:guid}/decisions", async (Guid id, HilmaDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ApprovalDecisions.AsNoTracking()
                .Where(d => d.AssessmentId == id)
                .OrderByDescending(d => d.DecidedAt)
                .ToListAsync(ct)))
        .WithName("GetDecisionHistory")
        .WithSummary("Every decision recorded against an assessment, newest first.");

        // The measurement the whole approval gate exists to make possible.
        group.MapGet("/metrics", async (HilmaDbContext db, CancellationToken ct) =>
        {
            var assessments = await db.FitAssessments.CountAsync(ct);
            if (assessments == 0) return Results.Ok(new { assessments = 0, note = "No assessments yet." });

            // Only the latest decision per assessment counts: earlier ones are history, not the verdict.
            var latest = await db.ApprovalDecisions.AsNoTracking()
                .GroupBy(d => d.AssessmentId)
                .Select(g => g.OrderByDescending(d => d.DecidedAt).First())
                .ToListAsync(ct);

            var reviewed = latest.Count;
            var modelVsScore = await db.FitAssessments.CountAsync(a => a.RecommendationDisagreement, ct);

            return Results.Ok(new
            {
                assessments,
                reviewed,
                pending = assessments - reviewed,

                // How often the humans changed the agent's answer. The headline quality number.
                overrides = latest.Count(d => d.Decision != DecisionType.Approved),
                overrideRate = reviewed == 0 ? (double?)null : Math.Round((double)latest.Count(d => d.Decision != DecisionType.Approved) / reviewed, 3),
                approvalRate = reviewed == 0 ? (double?)null : Math.Round((double)latest.Count(d => d.Decision == DecisionType.Approved) / reviewed, 3),

                byDecision = latest.GroupBy(d => d.Decision).ToDictionary(g => g.Key, g => g.Count()),

                // A separate signal from human overrides: how often the model and the deterministic
                // score reached different conclusions from the same numbers.
                modelScoreDisagreements = modelVsScore,
                modelScoreDisagreementRate = Math.Round((double)modelVsScore / assessments, 3),
            });
        })
        .WithName("GetMetrics")
        .WithSummary("Approval rate, override rate, and how often the model and the score disagree.");

        return app;
    }
}
