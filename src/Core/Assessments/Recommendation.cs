namespace HilmaAgent.Core.Assessments;

public static class Recommendation
{
    public const string Go = "GO";
    public const string NoGo = "NO_GO";
    public const string Investigate = "INVESTIGATE";

    public static bool IsValid(string? value) => value is Go or NoGo or Investigate;

    /// <summary>
    /// What the deterministic score alone implies. The model may reach a different conclusion with
    /// the same numbers — that disagreement is surfaced, not resolved silently.
    /// </summary>
    public static string FromScore(ScoreBreakdown breakdown) => breakdown switch
    {
        { IsBlocked: true } => NoGo,
        { Total: >= GoThreshold } => Go,
        { Total: >= InvestigateThreshold } => Investigate,
        _ => NoGo,
    };

    /// <summary>
    /// Thresholds are deliberately coarse. They exist to make the score's implication explicit and
    /// comparable, not to be precise — the human approval gate is what actually decides.
    /// </summary>
    public const int GoThreshold = 65;

    public const int InvestigateThreshold = 40;
}
