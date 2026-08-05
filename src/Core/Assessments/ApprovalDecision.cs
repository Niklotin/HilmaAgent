namespace HilmaAgent.Core.Assessments;

public static class DecisionType
{
    /// <summary>The reviewer accepted the agent's recommendation as it stood.</summary>
    public const string Approved = "APPROVED";

    /// <summary>The reviewer rejected the recommendation outright.</summary>
    public const string Rejected = "REJECTED";

    /// <summary>The reviewer kept the assessment but changed its recommendation.</summary>
    public const string Edited = "EDITED";

    public static bool IsValid(string? value) => value is Approved or Rejected or Edited;
}

/// <summary>
/// A human's verdict on one agent proposal.
/// </summary>
/// <remarks>
/// <para>This table is the point of the project. Nothing reaches a shortlist without a row here, so
/// it is both the audit trail — who decided what, when, and why — and the dataset that makes agent
/// quality measurable rather than asserted: override rate, approval rate, and how often the humans
/// side with the deterministic score against the model.</para>
/// <para>Decisions are append-only. Changing your mind writes a new row rather than editing the old
/// one, because an audit trail you can quietly revise is not an audit trail.</para>
/// </remarks>
public class ApprovalDecision
{
    public Guid Id { get; set; }

    public Guid AssessmentId { get; set; }
    public FitAssessment? Assessment { get; set; }

    /// <summary>See <see cref="DecisionType"/>.</summary>
    public required string Decision { get; set; }

    /// <summary>
    /// The reviewer's replacement recommendation, set only when <see cref="Decision"/> is EDITED.
    /// The agent's original is never overwritten — both are kept so the disagreement stays visible.
    /// </summary>
    public string? EditedRecommendation { get; set; }

    /// <summary>Why. Free text, and the most useful column here when reading back what went wrong.</summary>
    public string? ReviewerNote { get; set; }

    /// <summary>
    /// Who decided. Single-user for now, so this is a label rather than a foreign key — deliberately
    /// not a user table, because role hierarchies are out of scope.
    /// </summary>
    public string? ReviewedBy { get; set; }

    public DateTimeOffset DecidedAt { get; set; }

    /// <summary>
    /// What the reviewer ultimately stood behind: their edit if they made one, otherwise the agent's
    /// recommendation. This is the column a shortlist query filters on.
    /// </summary>
    public string EffectiveRecommendation(string modelRecommendation) =>
        Decision == DecisionType.Edited && EditedRecommendation is { } edited ? edited : modelRecommendation;
}
