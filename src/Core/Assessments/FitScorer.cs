using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;

namespace HilmaAgent.Core.Assessments;

/// <summary>
/// Scores a notice against a company profile. Pure, deterministic, and the only thing allowed to
/// produce a number — the LLM narrates this output and never generates it.
/// </summary>
/// <remarks>
/// <para>Being a pure function of (notice, profile, clock) is what makes the system auditable: the
/// same inputs always give the same score, a score can be replayed months later, and a disagreement
/// between the model and the score is meaningful rather than noise.</para>
/// <para>The weights below are a judgement, not a fact. They are stated here so they can be argued
/// with — which is the point of computing them in code rather than asking a model to feel them out.</para>
/// </remarks>
public class FitScorer(TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>CPV overlap dominates: procurement is classified by CPV, and it is the strongest signal of fit.</summary>
    public const int CpvMax = 45;

    /// <summary>Region matters, but a capable supplier travels. Weighted below CPV for that reason.</summary>
    public const int RegionMax = 20;

    /// <summary>Contract value gates capacity in both directions — too small to bother, too large to deliver.</summary>
    public const int ValueMax = 20;

    /// <summary>Time to prepare a bid. A tender closing in three days is worth less than one closing in six weeks.</summary>
    public const int DeadlineMax = 15;

    public ScoreBreakdown Score(Notice notice, CompanyProfile profile)
    {
        var rules = new List<ScoreRule>();
        var gates = new List<ScoreGate>();
        var warnings = new List<string>();

        ApplyGates(notice, gates);

        rules.Add(ScoreCpv(notice, profile));
        rules.Add(ScoreRegion(notice, profile));
        rules.Add(ScoreValue(notice, profile, warnings));
        rules.Add(ScoreDeadline(notice, warnings));

        // A blocked notice scores zero rather than carrying a misleading number into the UI: a
        // closed tender with perfect CPV overlap is worth nothing, and showing "88" would suggest
        // otherwise at a glance.
        var total = gates.Count > 0 ? 0 : rules.Sum(rule => rule.Awarded);

        return new ScoreBreakdown(total, rules, gates, warnings);
    }

    /// <summary>Conditions under which no score is worth computing.</summary>
    private void ApplyGates(Notice notice, List<ScoreGate> gates)
    {
        var now = _time.GetUtcNow();

        if (notice.SubmissionDeadline is { } deadline && deadline <= now)
            gates.Add(new ScoreGate("deadline_passed",
                $"Submission deadline passed on {deadline:yyyy-MM-dd}; the tender can no longer be bid."));

        if (notice.IsCancelled == true)
            gates.Add(new ScoreGate("cancelled", "The notice has been cancelled by the buyer."));

        // Award and prior-information notices announce outcomes or intentions. They are useful
        // market intelligence and worth indexing, but there is nothing to bid on.
        if (notice.NoticeType is { } type && NotBiddable.Any(marker => type.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            gates.Add(new ScoreGate("not_biddable",
                $"Notice type '{type}' announces a result or an intention rather than an open tender."));
    }

    private static readonly string[] NotBiddable =
        ["ContractAward", "Award", "PriorInformation", "DesignContestResult", "Modification", "Transparency"];

    /// <summary>
    /// CPV is hierarchical — 45233220 (road surfacing) sits under 45230000 (construction of
    /// pipelines/roads) under 45000000 (construction). Matching on string equality would miss every
    /// near neighbour, so the score is graded by how deep the deepest shared prefix runs.
    /// </summary>
    private static ScoreRule ScoreCpv(Notice notice, CompanyProfile profile)
    {
        if (profile.PreferredCpvCodes.Count == 0)
            return new ScoreRule("cpv_overlap", 0, CpvMax, "Profile declares no CPV codes; no overlap can be computed.");

        if (notice.CpvCodes.Count == 0)
            return new ScoreRule("cpv_overlap", 0, CpvMax, "Notice carries no CPV codes.");

        var best = 0;
        string? bestPair = null;

        foreach (var noticeCode in notice.CpvCodes)
        foreach (var profileCode in profile.PreferredCpvCodes)
        {
            var (awarded, relation) = CompareCpv(noticeCode, profileCode);
            if (awarded <= best) continue;
            best = awarded;
            bestPair = $"{noticeCode} vs {profileCode} ({relation})";
        }

        return new ScoreRule("cpv_overlap", best, CpvMax,
            best > 0
                ? $"Best CPV match: {bestPair}."
                : $"No shared CPV division between notice ({string.Join(", ", notice.CpvCodes)}) and profile.");
    }

    /// <summary>
    /// Trailing zeros in a CPV code denote breadth, not digits: 72000000 is the whole IT-services
    /// division, 72200000 the software programming and consultancy group inside it. Comparing the
    /// raw strings makes those two look like a two-digit match, when in fact one contains the other.
    /// So both codes are reduced to their significant prefix first, and containment is scored as the
    /// strong signal it is.
    /// </summary>
    private static (int Awarded, string Relation) CompareCpv(string noticeCode, string profileCode)
    {
        var notice = SignificantPrefix(noticeCode);
        var profile = SignificantPrefix(profileCode);

        if (notice.Length == 0 || profile.Length == 0) return (0, "no comparable digits");

        if (notice == profile) return (CpvMax, "exact");

        // Containment: the notice's category sits inside the company's declared area, or vice versa.
        if (notice.StartsWith(profile, StringComparison.Ordinal) || profile.StartsWith(notice, StringComparison.Ordinal))
        {
            var depth = Math.Min(notice.Length, profile.Length);
            var awarded = depth switch
            {
                >= 5 => CpvMax,
                4 => 40,
                3 => 36,
                _ => 30,   // division-level: broad, but the notice genuinely falls in the declared area
            };

            return (awarded, $"contained at {depth} significant digits");
        }

        // Siblings: related but neither contains the other, e.g. 72200000 vs 72590000.
        var shared = SharedPrefixLength(notice, profile);
        var siblingAward = shared switch
        {
            >= 5 => 38,
            4 => 30,
            3 => 20,
            2 => 10,
            _ => 0,
        };

        return (siblingAward, shared > 0 ? $"related, {shared} shared digits" : "unrelated");
    }

    /// <summary>Strips the trailing zeros that pad a CPV code to eight digits, keeping the meaningful part.</summary>
    private static string SignificantPrefix(string cpvCode)
    {
        var trimmed = cpvCode.Split('-')[0].TrimEnd('0');
        // A code that is all zeros, or a division ending in zero such as 45000000 -> "45", is still
        // meaningful at two digits; never trim below the division.
        return trimmed.Length >= 2 ? trimmed : cpvCode.Length >= 2 ? cpvCode[..2] : string.Empty;
    }

    private static int SharedPrefixLength(string a, string b)
    {
        var length = Math.Min(a.Length, b.Length);
        var shared = 0;
        while (shared < length && a[shared] == b[shared]) shared++;
        return shared;
    }

    /// <summary>
    /// NUTS is hierarchical: FI1B1 (Helsinki) sits inside FI1B inside FI1 inside FI. A profile that
    /// declares "FI1B" is claiming the whole area, so a notice in FI1B1 is fully inside it — scoring
    /// that as a partial match would penalise the profile for describing its region correctly.
    /// </summary>
    private static ScoreRule ScoreRegion(Notice notice, CompanyProfile profile)
    {
        if (profile.Regions.Count == 0)
            return new ScoreRule("region_match", RegionMax, RegionMax, "Profile declares no region limit; treated as nationwide.");

        if (notice.Region.Count == 0)
            return new ScoreRule("region_match", RegionMax / 2, RegionMax, "Notice declares no region; cannot be excluded on geography.");

        var best = 0;
        string? bestPair = null;

        foreach (var noticeRegion in notice.Region)
        foreach (var profileRegion in profile.Regions)
        {
            var a = noticeRegion.ToUpperInvariant();
            var b = profileRegion.ToUpperInvariant();

            int awarded;
            string relation;

            if (a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal))
            {
                awarded = RegionMax;
                relation = "contained";
            }
            else
            {
                var shared = SharedPrefixLength(a, b);
                awarded = shared switch
                {
                    >= 4 => 16,
                    3 => 12,
                    2 => 8,   // same country, different area — a supplier can travel
                    _ => 0,
                };
                relation = shared > 0 ? $"{shared} shared characters" : "unrelated";
            }

            if (awarded <= best) continue;
            best = awarded;
            bestPair = $"{noticeRegion} vs {profileRegion} ({relation})";
        }

        return new ScoreRule("region_match", best, RegionMax,
            best > 0
                ? $"Best region match: {bestPair}."
                : $"Notice regions ({string.Join(", ", notice.Region)}) fall outside the profile's areas.");
    }

    /// <summary>
    /// Value is frequently missing, and an absent value is not a small value. Unknown scores
    /// mid-band and raises a warning rather than being treated as either a fit or a miss.
    /// </summary>
    private static ScoreRule ScoreValue(Notice notice, CompanyProfile profile, List<string> warnings)
    {
        if (profile.MinContractValue is null && profile.MaxContractValue is null)
            return new ScoreRule("value_band", ValueMax, ValueMax, "Profile declares no contract value limits.");

        var value = notice.EstimatedValue
            // A range: take its midpoint, which is the least wrong single number to compare.
            ?? (notice.EstimatedValueMin is { } min && notice.EstimatedValueMax is { } max ? (min + max) / 2 : null);

        if (value is null)
        {
            warnings.Add(notice.EstimatedValueWithheld
                ? "The buyer withheld the contract value, so value fit could not be assessed."
                : "The notice states no contract value, so value fit could not be assessed.");

            return new ScoreRule("value_band", ValueMax / 2, ValueMax, "Contract value unknown; scored neutrally.");
        }

        var below = profile.MinContractValue is { } floor && value < floor;
        var above = profile.MaxContractValue is { } ceiling && value > ceiling;

        if (!below && !above)
            return new ScoreRule("value_band", ValueMax, ValueMax, $"Estimated value {value:N0} is within the profile's band.");

        // Near-misses still score: a contract 20% over the ceiling may be deliverable with a partner,
        // whereas one ten times over is not. The cliff is at 2x.
        var reference = below ? profile.MinContractValue!.Value : profile.MaxContractValue!.Value;
        var ratio = below ? value.Value / Math.Max(reference, 1) : reference / Math.Max(value.Value, 1);
        var awarded = ratio >= 0.5m ? ValueMax / 2 : 0;

        return new ScoreRule("value_band", awarded, ValueMax,
            $"Estimated value {value:N0} falls {(below ? "below" : "above")} the profile's " +
            $"{(below ? "minimum" : "maximum")} of {reference:N0}.");
    }

    private ScoreRule ScoreDeadline(Notice notice, List<string> warnings)
    {
        if (notice.SubmissionDeadline is not { } deadline)
        {
            warnings.Add("The notice states no submission deadline; time to bid could not be assessed.");
            return new ScoreRule("deadline_headroom", DeadlineMax / 2, DeadlineMax, "No submission deadline stated.");
        }

        var days = (deadline - _time.GetUtcNow()).TotalDays;
        var awarded = days switch
        {
            >= 28 => DeadlineMax,
            >= 14 => 12,
            >= 7 => 8,
            > 0 => 4,
            _ => 0,
        };

        if (days is > 0 and < 7)
            warnings.Add($"Only {days:F0} day(s) left to submit; preparation time is very short.");

        return new ScoreRule("deadline_headroom", awarded, DeadlineMax,
            days > 0
                ? $"{days:F0} day(s) until the {deadline:yyyy-MM-dd} deadline."
                : $"Deadline {deadline:yyyy-MM-dd} has passed.");
    }
}
