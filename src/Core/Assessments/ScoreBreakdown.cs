namespace HilmaAgent.Core.Assessments;

/// <summary>
/// Stable identifiers for every sentence the scorer can produce.
/// </summary>
/// <remarks>
/// The scorer's prose is English and is written into the stored assessment, which is never
/// rewritten. Emitting a code and its arguments alongside that prose lets the UI render the same
/// fact in Finnish without touching a single stored record: assessments written before this existed
/// carry only the sentence, and the reader falls back to it.
/// </remarks>
public static class ScoreCodes
{
    public const string CpvNoProfileCodes = "cpv.no_profile_codes";
    public const string CpvNoNoticeCodes = "cpv.no_notice_codes";
    public const string CpvBestMatch = "cpv.best_match";
    public const string CpvNoOverlap = "cpv.no_overlap";

    public const string RegionNoLimit = "region.no_limit";
    public const string RegionNoticeUnstated = "region.notice_unstated";
    public const string RegionBestMatch = "region.best_match";
    public const string RegionOutside = "region.outside";

    public const string ValueNoLimits = "value.no_limits";
    public const string ValueUnknown = "value.unknown";
    public const string ValueWithinBand = "value.within_band";
    public const string ValueBelowMinimum = "value.below_minimum";
    public const string ValueAboveMaximum = "value.above_maximum";

    public const string DeadlineNone = "deadline.none";
    public const string DeadlineDaysLeft = "deadline.days_left";
    public const string DeadlinePassed = "deadline.passed";

    public const string GateDeadlinePassed = "gate.deadline_passed";
    public const string GateCancelled = "gate.cancelled";
    public const string GateNotBiddable = "gate.not_biddable";

    public const string WarnValueWithheld = "warn.value_withheld";
    public const string WarnValueUnstated = "warn.value_unstated";
    public const string WarnDeadlineUnstated = "warn.deadline_unstated";
    public const string WarnDeadlineImminent = "warn.deadline_imminent";

    /// <summary>Relations a CPV or region comparison can report, as codes rather than words.</summary>
    public const string RelationExact = "exact";
    public const string RelationContained = "contained";
    public const string RelationRelated = "related";
    public const string RelationUnrelated = "unrelated";
}

/// <summary>One scoring rule's contribution, recorded so a score can be audited line by line.</summary>
/// <param name="Rule">Stable identifier, e.g. "cpv_overlap".</param>
/// <param name="Awarded">Points given.</param>
/// <param name="Max">Points available. Awarded/Max is the rule's satisfaction.</param>
/// <param name="Detail">
/// Human-readable reason in English, shown where no translation exists and given to the model as
/// context. Kept verbatim in the stored record.
/// </param>
/// <param name="DetailCode">See <see cref="ScoreCodes"/>. Null on assessments written before codes existed.</param>
/// <param name="DetailArgs">Values to substitute into the translated sentence.</param>
public sealed record ScoreRule(
    string Rule,
    int Awarded,
    int Max,
    string Detail,
    string? DetailCode = null,
    IReadOnlyDictionary<string, string>? DetailArgs = null);

/// <summary>
/// A blocking condition. Gates are not points — they force the recommendation regardless of score,
/// because no amount of CPV overlap makes a closed tender biddable.
/// </summary>
/// <param name="Gate">Stable identifier, e.g. "deadline_passed".</param>
/// <param name="Detail">Why it fired, in English.</param>
public sealed record ScoreGate(
    string Gate,
    string Detail,
    string? DetailCode = null,
    IReadOnlyDictionary<string, string>? DetailArgs = null);

/// <summary>A fact that weakens confidence without blocking.</summary>
/// <param name="Text">English sentence — what the model is shown, and what older records hold.</param>
public sealed record ScoreNote(
    string Text,
    string? Code = null,
    IReadOnlyDictionary<string, string>? Args = null);

/// <param name="Total">0–100, the sum of rule awards. Zero when a gate fires.</param>
/// <param name="Rules">Every rule considered, including those that scored nothing.</param>
/// <param name="Gates">Blocking conditions that fired. Non-empty means NO_GO.</param>
/// <param name="Warnings">Facts that weaken confidence without blocking, e.g. a withheld contract value.</param>
public sealed record ScoreBreakdown(
    int Total,
    IReadOnlyList<ScoreRule> Rules,
    IReadOnlyList<ScoreGate> Gates,
    IReadOnlyList<ScoreNote> Warnings)
{
    public bool IsBlocked => Gates.Count > 0;
}
