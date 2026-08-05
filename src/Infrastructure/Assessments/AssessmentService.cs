using System.Text.Json;
using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using HilmaAgent.Infrastructure.Persistence;
using HilmaAgent.Infrastructure.Profiles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace HilmaAgent.Infrastructure.Assessments;

/// <summary>Progress events, surfaced over SSE so the UI shows work happening rather than a spinner.</summary>
/// <param name="Step">Stable identifier: loading, scoring, retrieving, narrating, validating, done.</param>
public sealed record AssessmentProgress(string Step, string Detail, object? Data = null);

/// <summary>
/// Produces one assessment: score it in code, retrieve supporting passages, have the model narrate,
/// then verify what came back.
/// </summary>
/// <remarks>
/// The order is the point. Scoring happens before retrieval and before the model is involved, so the
/// number cannot be influenced by what the model saw or said. The model's only inputs are a finished
/// score and a fixed set of passages, and its output is checked against both.
/// </remarks>
public class AssessmentService(
    HilmaDbContext db,
    FitScorer scorer,
    IAssessmentNarrator narrator,
    ILogger<AssessmentService> logger,
    // Optional: used only to order the notice's own passages by relevance to the company. The
    // assessment is correct without it, which is why it is allowed to be absent.
    Retrieval.NoticeSearchService? search = null)
{
    private static readonly JsonSerializerOptions BreakdownJson = new(JsonSerializerDefaults.Web);

    public async Task<FitAssessment> AssessAsync(
        string noticeId,
        Guid? profileId = null,
        IProgress<AssessmentProgress>? progress = null,
        CancellationToken ct = default)
    {
        progress?.Report(new AssessmentProgress("loading", $"Loading notice {noticeId}."));

        var notice = await db.Notices.AsNoTracking().FirstOrDefaultAsync(n => n.Id == noticeId, ct)
            ?? throw new KeyNotFoundException($"Notice '{noticeId}' is not in the store. Ingest it first.");

        var profile = await LoadProfileAsync(profileId, ct);

        // Deterministic first, and independent of everything downstream.
        progress?.Report(new AssessmentProgress("scoring", $"Scoring against {profile.Name}."));
        var breakdown = scorer.Score(notice, profile);
        var scoreRecommendation = Recommendation.FromScore(breakdown);

        progress?.Report(new AssessmentProgress("scoring",
            $"Score {breakdown.Total}/100 implies {scoreRecommendation}.",
            new { breakdown.Total, recommendation = scoreRecommendation, gates = breakdown.Gates }));

        progress?.Report(new AssessmentProgress("retrieving", "Retrieving supporting passages."));
        var sources = await RetrieveSourcesAsync(notice, profile, ct);
        progress?.Report(new AssessmentProgress("retrieving", $"Retrieved {sources.Count} passage(s)."));

        progress?.Report(new AssessmentProgress("narrating", $"Asking {narrator.ModelId} for a justification."));
        var narration = await narrator.NarrateAsync(notice, profile, breakdown, sources, ct);

        progress?.Report(new AssessmentProgress("validating", "Checking the model's output against the score."));

        var disagreement = narration.Recommendation != scoreRecommendation;
        if (disagreement)
            logger.LogWarning(
                "Model recommended {Model} where the score implies {Score} for notice {NoticeId}; flagged for review.",
                narration.Recommendation, scoreRecommendation, noticeId);

        var byId = sources.ToDictionary(source => source.ChunkId);

        // Validate here, not only in the narrator: this is the layer that has to hold whichever
        // implementation is plugged in. A citation pointing at something the model was never given
        // is unverifiable, and an unverifiable citation is worse than none — it looks like evidence.
        var verified = narration.CitedChunkIds.Where(byId.ContainsKey).Distinct().ToList();
        var invented = narration.CitedChunkIds.Count - verified.Count;
        if (invented > 0)
            logger.LogWarning("Dropped {Count} citation(s) from {Model} that referenced unsupplied chunks on {NoticeId}.",
                invented, narrator.ModelId, noticeId);

        var assessment = new FitAssessment
        {
            Id = Guid.NewGuid(),
            NoticeId = notice.Id,
            ProfileId = profile.Id,
            DeterministicScore = breakdown.Total,
            // Web defaults, so the stored breakdown uses the same camelCase as every API response.
            // Without this the column is PascalCase while the wire format is camelCase, and anything
            // reading the stored JSON has to know which of the two it is looking at.
            ScoreBreakdownJson = JsonSerializer.Serialize(breakdown, BreakdownJson),
            ScoreRecommendation = scoreRecommendation,
            ModelRecommendation = narration.Recommendation,
            RecommendationDisagreement = disagreement,
            Reasoning = narration.Reasoning,
            Citations = verified
                .Select(id => new Citation(id, byId[id].Section, Excerpt(byId[id].Content)))
                .ToList(),
            ModelId = narrator.ModelId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.FitAssessments.Add(assessment);
        await db.SaveChangesAsync(ct);

        progress?.Report(new AssessmentProgress("done",
            disagreement
                ? $"Done — model says {narration.Recommendation}, score says {scoreRecommendation}. Flagged."
                : $"Done — {narration.Recommendation}.",
            new { assessment.Id, assessment.DeterministicScore, assessment.ModelRecommendation, assessment.RecommendationDisagreement }));

        return assessment;
    }

    private async Task<CompanyProfile> LoadProfileAsync(Guid? profileId, CancellationToken ct)
    {
        if (profileId is { } id)
            return await db.CompanyProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct)
                ?? throw new KeyNotFoundException($"Company profile '{id}' not found.");

        return await db.CompanyProfiles.AsNoTracking().OrderBy(p => p.CreatedAt).FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("No company profile exists. Seed one before assessing.");
    }

    /// <summary>
    /// Gathers the passages the model may cite: this notice's own chunks, plus a profile-driven
    /// semantic search restricted to the same notice. The notice's own text is always included so a
    /// weak retrieval result never leaves the model with nothing to point at.
    /// </summary>
    private async Task<IReadOnlyList<NarrationSource>> RetrieveSourcesAsync(
        Notice notice,
        CompanyProfile profile,
        CancellationToken ct)
    {
        var own = await db.NoticeChunks.AsNoTracking()
            .Where(chunk => chunk.NoticeId == notice.Id)
            .OrderBy(chunk => chunk.ChunkIndex)
            .Select(chunk => new NarrationSource(chunk.Id, chunk.Section, chunk.LotId, chunk.Content))
            .ToListAsync(ct);

        if (own.Count == 0)
            logger.LogWarning("Notice {NoticeId} has no chunks; the model will have nothing to cite.", notice.Id);

        // Rank this notice's own passages by how well they match what the company does, so the most
        // relevant lot leads when a notice bundles several.
        var query = string.Join(" ", new[] { profile.Description, string.Join(", ", profile.Technologies) }
            .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (search is null || string.IsNullOrWhiteSpace(query) || own.Count <= 1) return own;

        try
        {
            var ranked = await search.SearchAsync(query, limit: 25, ct: ct);
            var order = ranked
                .FirstOrDefault(result => result.NoticeId == notice.Id)?.Chunks
                .Select((chunk, index) => (chunk.ChunkId, index))
                .ToDictionary(pair => pair.ChunkId, pair => pair.index);

            if (order is { Count: > 0 })
                return own.OrderBy(source => order.GetValueOrDefault(source.ChunkId, int.MaxValue)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Ranking is an improvement, not a requirement — the notice's own chunks are enough.
            logger.LogWarning(ex, "Could not rank passages for {NoticeId}; using document order.", notice.Id);
        }

        return own;
    }

    private static string Excerpt(string content) =>
        content.Length <= 300 ? content : content[..300] + "…";

    /// <summary>Creates the demo profile if the store is empty. Called at startup.</summary>
    public static async Task EnsureSeedProfileAsync(HilmaDbContext db, CancellationToken ct = default)
    {
        if (await db.CompanyProfiles.AnyAsync(ct)) return;

        var profile = SeedProfile.Create();
        profile.CreatedAt = profile.UpdatedAt = DateTimeOffset.UtcNow;
        db.CompanyProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
    }
}
