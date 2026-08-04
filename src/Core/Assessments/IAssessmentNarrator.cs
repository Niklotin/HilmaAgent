using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;

namespace HilmaAgent.Core.Assessments;

/// <param name="ChunkId">Identifier the model must cite by. Supplied to it; anything else is rejected.</param>
public sealed record NarrationSource(Guid ChunkId, string Section, string? LotId, string Content);

/// <param name="Recommendation">GO / NO_GO / INVESTIGATE, chosen by the model.</param>
/// <param name="Reasoning">The justification, written over the supplied score.</param>
/// <param name="CitedChunkIds">Chunks the model pointed at. Validated against what it was given.</param>
public sealed record Narration(string Recommendation, string Reasoning, IReadOnlyList<Guid> CitedChunkIds);

/// <summary>
/// Writes the human-readable justification for an already-computed score.
/// </summary>
/// <remarks>
/// The narrow signature is the design: the implementation receives a finished
/// <see cref="ScoreBreakdown"/> and cannot alter it. The model's job is to explain a number and pick
/// a recommendation, not to produce the number — so a change of provider changes the prose and never
/// the ranking.
/// </remarks>
public interface IAssessmentNarrator
{
    /// <summary>Model identifier, stored on the assessment so it stays interpretable after a model change.</summary>
    string ModelId { get; }

    Task<Narration> NarrateAsync(
        Notice notice,
        CompanyProfile profile,
        ScoreBreakdown breakdown,
        IReadOnlyList<NarrationSource> sources,
        CancellationToken ct = default);
}
