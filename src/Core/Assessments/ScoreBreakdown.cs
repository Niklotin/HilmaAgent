namespace HilmaAgent.Core.Assessments;

/// <summary>One scoring rule's contribution, recorded so a score can be audited line by line.</summary>
/// <param name="Rule">Stable identifier, e.g. "cpv_overlap".</param>
/// <param name="Awarded">Points given.</param>
/// <param name="Max">Points available. Awarded/Max is the rule's satisfaction.</param>
/// <param name="Detail">Human-readable reason, shown in the UI and given to the model as context.</param>
public sealed record ScoreRule(string Rule, int Awarded, int Max, string Detail);

/// <summary>
/// A blocking condition. Gates are not points — they force the recommendation regardless of score,
/// because no amount of CPV overlap makes a closed tender biddable.
/// </summary>
/// <param name="Gate">Stable identifier, e.g. "deadline_passed".</param>
/// <param name="Detail">Why it fired.</param>
public sealed record ScoreGate(string Gate, string Detail);

/// <param name="Total">0–100, the sum of rule awards. Zero when a gate fires.</param>
/// <param name="Rules">Every rule considered, including those that scored nothing.</param>
/// <param name="Gates">Blocking conditions that fired. Non-empty means NO_GO.</param>
/// <param name="Warnings">Facts that weaken confidence without blocking, e.g. a withheld contract value.</param>
public sealed record ScoreBreakdown(
    int Total,
    IReadOnlyList<ScoreRule> Rules,
    IReadOnlyList<ScoreGate> Gates,
    IReadOnlyList<string> Warnings)
{
    public bool IsBlocked => Gates.Count > 0;
}
