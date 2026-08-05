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
        group.MapGet("/queue", async (
            HilmaDbContext db,
            CancellationToken ct,
            int take = 25,
            bool disagreementsFirst = true,
            bool disagreementsOnly = false,
            int? minScore = null,
            string? sort = null) =>
        {
            var decided = db.ApprovalDecisions.Select(d => d.AssessmentId);

            var query = db.FitAssessments.AsNoTracking()
                .Where(a => !decided.Contains(a.Id));

            if (disagreementsOnly) query = query.Where(a => a.RecommendationDisagreement);
            if (minScore is { } floor) query = query.Where(a => a.DeterministicScore >= floor);

            query = sort switch
            {
                // Urgency, not quality: a GO closing in three days needs a decision more than a
                // better one closing in six weeks. Notices with no stated deadline sort last —
                // they are the ones with no clock running.
                "deadline" => query
                    .OrderBy(a => a.Notice!.SubmissionDeadline == null)
                    .ThenBy(a => a.Notice!.SubmissionDeadline)
                    .ThenByDescending(a => a.DeterministicScore),

                "score" => query.OrderByDescending(a => a.DeterministicScore),

                // Default, and the historical behaviour: the cases where the model and the rules
                // disagree are where a human's judgement is actually required.
                _ => disagreementsFirst
                    ? query.OrderByDescending(a => a.RecommendationDisagreement).ThenByDescending(a => a.DeterministicScore)
                    : query.OrderByDescending(a => a.DeterministicScore),
            };

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
        .WithSummary("Assessments awaiting a human decision. Sort by disagreement (default), score, or deadline.");

        // The other half of the queue. Without this the audit trail is write-only: /queue excludes
        // anything decided, so a recorded decision — and the note explaining it — would be
        // unreachable the moment it was made. Decisions being append-only is only worth something
        // if the history can be read back.
        group.MapGet("/decided", async (HilmaDbContext db, CancellationToken ct, int take = 50) =>
        {
            take = Math.Clamp(take, 1, 100);

            // Only the latest decision per assessment is the current verdict; earlier ones are
            // history, and travel with the assessment as a count so the UI can offer to expand them.
            var latest = await db.ApprovalDecisions.AsNoTracking()
                .GroupBy(d => d.AssessmentId)
                .Select(g => g.OrderByDescending(d => d.DecidedAt).First())
                .ToListAsync(ct);

            if (latest.Count == 0) return Results.Ok(Array.Empty<object>());

            var revisions = await db.ApprovalDecisions.AsNoTracking()
                .GroupBy(d => d.AssessmentId)
                .Select(g => new { AssessmentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.AssessmentId, x => x.Count, ct);

            var ids = latest.Select(d => d.AssessmentId).ToList();

            var assessments = await db.FitAssessments.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
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
                .ToListAsync(ct);

            var byId = assessments.ToDictionary(a => a.Id);

            return Results.Ok(latest
                .Where(decision => byId.ContainsKey(decision.AssessmentId))
                .OrderByDescending(decision => decision.DecidedAt)
                .Take(take)
                .Select(decision =>
                {
                    var a = byId[decision.AssessmentId];
                    return new
                    {
                        a.Id,
                        a.NoticeId,
                        a.noticeTitle,
                        a.buyerName,
                        a.SubmissionDeadline,
                        a.EstimatedValue,
                        a.Currency,
                        a.DeterministicScore,
                        a.ScoreRecommendation,
                        a.ModelRecommendation,
                        a.RecommendationDisagreement,
                        a.Reasoning,
                        a.Citations,
                        a.ModelId,
                        a.CreatedAt,

                        decision = decision.Decision,
                        decision.EditedRecommendation,
                        decision.ReviewerNote,
                        decision.ReviewedBy,
                        decision.DecidedAt,

                        // What the reviewer actually stood behind, which is the column a shortlist
                        // would filter on — an EDITED decision replaces the model's recommendation.
                        effectiveRecommendation = decision.EffectiveRecommendation(a.ModelRecommendation),
                        revisionCount = revisions.GetValueOrDefault(decision.AssessmentId, 1),
                    };
                })
                .ToList());
        })
        .WithName("GetDecidedAssessments")
        .WithSummary("Assessments a human has ruled on, newest decision first.");

        // What the reviewer said yes to and can still act on.
        //
        // Approving something was, until this existed, the end of the road: a row was written, the
        // card left the queue, and the reviewer was on their own to go and find the tender again.
        // The shortlist closes that loop — still-open notices the human backed, soonest deadline
        // first, each with the link to where bids are actually submitted.
        group.MapGet("/shortlist", async (HilmaDbContext db, CancellationToken ct, int take = 50) =>
        {
            take = Math.Clamp(take, 1, 100);

            var latest = await db.ApprovalDecisions.AsNoTracking()
                .GroupBy(d => d.AssessmentId)
                .Select(g => g.OrderByDescending(d => d.DecidedAt).First())
                .ToListAsync(ct);

            // A rejection is never a shortlist entry, whatever the agent had recommended.
            var backed = latest.Where(d => d.Decision != DecisionType.Rejected).ToList();
            if (backed.Count == 0) return Results.Ok(Array.Empty<object>());

            var ids = backed.Select(d => d.AssessmentId).ToList();

            var assessments = await db.FitAssessments.AsNoTracking()
                .Where(a => ids.Contains(a.Id))
                .Select(a => new
                {
                    a.Id,
                    a.NoticeId,
                    noticeTitle = a.Notice!.Title,
                    buyerName = a.Notice.BuyerName,
                    a.Notice.SubmissionDeadline,
                    a.Notice.EstimatedValue,
                    a.Notice.Currency,
                    a.Notice.ProcurementDocumentsUrl,
                    a.DeterministicScore,
                    a.ModelRecommendation,
                })
                .ToListAsync(ct);

            var byId = assessments.ToDictionary(a => a.Id);
            var now = DateTimeOffset.UtcNow;

            return Results.Ok(backed
                .Where(decision => byId.ContainsKey(decision.AssessmentId))
                .Select(decision => new { decision, assessment = byId[decision.AssessmentId] })
                // Only what the reviewer stood behind as worth pursuing. An APPROVED NO-GO is a
                // recorded agreement not to bid, not a shortlist entry.
                .Where(x => x.decision.EffectiveRecommendation(x.assessment.ModelRecommendation) != Recommendation.NoGo)
                // A closed tender cannot be bid on, so it drops off rather than lingering as a
                // reproach. The decision itself stays readable in /decided either way.
                .Where(x => x.assessment.SubmissionDeadline is null || x.assessment.SubmissionDeadline > now)
                .OrderBy(x => x.assessment.SubmissionDeadline is null)
                .ThenBy(x => x.assessment.SubmissionDeadline)
                .Take(take)
                .Select(x => new
                {
                    assessmentId = x.assessment.Id,
                    x.assessment.NoticeId,
                    x.assessment.noticeTitle,
                    x.assessment.buyerName,
                    x.assessment.SubmissionDeadline,
                    x.assessment.EstimatedValue,
                    x.assessment.Currency,
                    x.assessment.DeterministicScore,

                    // Where a bid is actually submitted. Null for legacy notices, whose contract has
                    // no equivalent field — the UI says so rather than linking somewhere invented.
                    x.assessment.ProcurementDocumentsUrl,

                    standing = x.decision.EffectiveRecommendation(x.assessment.ModelRecommendation),
                    x.decision.ReviewedBy,
                    x.decision.DecidedAt,
                    x.decision.ReviewerNote,
                    daysLeft = x.assessment.SubmissionDeadline is { } due
                        ? (int)Math.Ceiling((due - now).TotalDays)
                        : (int?)null,
                })
                .ToList());
        })
        .WithName("GetShortlist")
        .WithSummary("Still-open notices a human backed, soonest deadline first.");

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
        .Produces<ApprovalDecision>(StatusCodes.Status201Created)
        .WithName("RecordDecision")
        .WithSummary("Records a human verdict on an assessment. Append-only.");

        group.MapGet("/assessments/{id:guid}/decisions", async (Guid id, HilmaDbContext db, CancellationToken ct) =>
            Results.Ok(await db.ApprovalDecisions.AsNoTracking()
                .Where(d => d.AssessmentId == id)
                .OrderByDescending(d => d.DecidedAt)
                .ToListAsync(ct)))
        .Produces<List<ApprovalDecision>>()
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
