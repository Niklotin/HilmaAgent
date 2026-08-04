using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;

namespace HilmaAgent.Core.Assessments;

/// <summary>A chunk the model cited in support of a claim.</summary>
/// <param name="ChunkId">Must be one of the chunks the model was given — validated, not trusted.</param>
/// <param name="Quote">The span the model pointed at, for display next to the claim.</param>
public sealed record Citation(Guid ChunkId, string? Section, string? Quote);

/// <summary>
/// One screening proposal: a score computed in code, and a narrative written over it by the model.
/// </summary>
public class FitAssessment
{
    public Guid Id { get; set; }

    public required string NoticeId { get; set; }
    public Notice? Notice { get; set; }

    public Guid ProfileId { get; set; }
    public CompanyProfile? Profile { get; set; }

    /// <summary>0–100, computed by <see cref="FitScorer"/>. Never produced by the model.</summary>
    public int DeterministicScore { get; set; }

    /// <summary>The full rule-by-rule breakdown, stored as jsonb so a score can be audited later.</summary>
    public required string ScoreBreakdownJson { get; set; }

    /// <summary>What the score alone implies — <see cref="Recommendation.FromScore"/>.</summary>
    public required string ScoreRecommendation { get; set; }

    /// <summary>What the model concluded. Usually the same; when it differs, that is the interesting case.</summary>
    public required string ModelRecommendation { get; set; }

    /// <summary>
    /// True when the model reached a different conclusion from the score. Surfaced in the UI rather
    /// than resolved: silently trusting either one would discard the signal.
    /// </summary>
    public bool RecommendationDisagreement { get; set; }

    /// <summary>The model's justification, written over the score.</summary>
    public required string Reasoning { get; set; }

    public List<Citation> Citations { get; set; } = [];

    /// <summary>Model that wrote the narrative, so an assessment stays interpretable after a model change.</summary>
    public string? ModelId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
