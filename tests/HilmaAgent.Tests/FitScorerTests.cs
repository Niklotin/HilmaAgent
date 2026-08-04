using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace HilmaAgent.Tests;

public class FitScorerTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    private static FitScorer Scorer() => new(new FakeTimeProvider(Now));

    private static CompanyProfile ItConsultancy() => new()
    {
        Name = "Example Oy",
        PreferredCpvCodes = ["72000000", "72200000"],
        Regions = ["FI1B"],
        MinContractValue = 50_000,
        MaxContractValue = 2_000_000,
    };

    private static Notice Notice(
        string? noticeType = "ContractNotices",
        IEnumerable<string>? cpv = null,
        IEnumerable<string>? region = null,
        decimal? value = 500_000,
        DateTimeOffset? deadline = null) => new()
    {
        Id = "EF-1",
        NoticeType = noticeType,
        CpvCodes = (cpv ?? ["72200000"]).ToList(),
        Region = (region ?? ["FI1B1"]).ToList(),
        EstimatedValue = value,
        SubmissionDeadline = deadline ?? Now.AddDays(30),
        Source = NoticeSource.EForms,
        RawPayload = "{}",
    };

    [Fact]
    public void A_strong_match_scores_near_the_top_and_recommends_go()
    {
        var breakdown = Scorer().Score(Notice(), ItConsultancy());

        breakdown.Total.ShouldBe(100);
        breakdown.IsBlocked.ShouldBeFalse();
        Recommendation.FromScore(breakdown).ShouldBe(Recommendation.Go);
    }

    private static int Cpv(ScoreBreakdown b) => b.Rules.Single(r => r.Rule == "cpv_overlap").Awarded;

    [Fact]
    public void Cpv_is_matched_hierarchically_not_by_equality()
    {
        var exact = Scorer().Score(Notice(cpv: ["72200000"]), ItConsultancy());
        var sibling = Scorer().Score(Notice(cpv: ["72590000"]), ItConsultancy());
        var unrelated = Scorer().Score(Notice(cpv: ["45233220"]), ItConsultancy());

        // String equality would score sibling and unrelated identically — both "no match" — which is
        // exactly the failure this grading exists to avoid.
        Cpv(exact).ShouldBe(FitScorer.CpvMax);
        Cpv(sibling).ShouldBeInRange(1, FitScorer.CpvMax - 1);
        Cpv(unrelated).ShouldBe(0);
    }

    [Fact]
    public void Trailing_zeros_mean_breadth_so_a_broad_profile_code_contains_a_specific_notice_code()
    {
        // 72000000 is the whole IT-services division; 72200000 sits inside it. Compared as raw
        // strings these share only "72" and would score as a weak division-level brush.
        var profile = new CompanyProfile { Name = "Broad Oy", PreferredCpvCodes = ["72000000"] };

        var contained = Scorer().Score(Notice(cpv: ["72200000"]), profile);
        var sibling = Scorer().Score(Notice(cpv: ["72590000"]), profile);

        Cpv(contained).ShouldBeGreaterThanOrEqualTo(30);
        // Both notices are inside division 72, so a profile claiming the division matches both.
        Cpv(sibling).ShouldBe(Cpv(contained));
    }

    [Fact]
    public void A_notice_region_inside_the_profile_region_is_a_full_match()
    {
        // Profile declares FI1B (Helsinki-Uusimaa); the notice is in FI1B1 (Helsinki), inside it.
        // Grading by shared-prefix length alone would dock points for declaring the area correctly.
        var rule = Scorer().Score(Notice(region: ["FI1B1"]), ItConsultancy())
            .Rules.Single(r => r.Rule == "region_match");

        rule.Awarded.ShouldBe(FitScorer.RegionMax);
        rule.Detail.ShouldContain("contained");
    }

    [Fact]
    public void A_passed_deadline_blocks_regardless_of_how_good_the_fit_is()
    {
        var breakdown = Scorer().Score(Notice(deadline: Now.AddDays(-1)), ItConsultancy());

        breakdown.IsBlocked.ShouldBeTrue();
        breakdown.Gates.ShouldContain(g => g.Gate == "deadline_passed");
        // Zero rather than a high number: a closed tender with perfect CPV overlap is worth nothing,
        // and showing "88" next to it would mislead at a glance.
        breakdown.Total.ShouldBe(0);
        Recommendation.FromScore(breakdown).ShouldBe(Recommendation.NoGo);
    }

    [Fact]
    public void Award_notices_are_blocked_because_there_is_nothing_to_bid_on()
    {
        var breakdown = Scorer().Score(Notice(noticeType: "ContractAwardNotices"), ItConsultancy());

        breakdown.IsBlocked.ShouldBeTrue();
        breakdown.Gates.ShouldContain(g => g.Gate == "not_biddable");
    }

    [Fact]
    public void A_cancelled_notice_is_blocked()
    {
        var notice = Notice();
        notice.IsCancelled = true;

        Scorer().Score(notice, ItConsultancy()).Gates.ShouldContain(g => g.Gate == "cancelled");
    }

    [Fact]
    public void Missing_value_scores_neutrally_and_warns_rather_than_counting_as_a_miss()
    {
        var breakdown = Scorer().Score(Notice(value: null), ItConsultancy());
        var rule = breakdown.Rules.Single(r => r.Rule == "value_band");

        // An absent value is not a small value. Treating it as either extreme would be a fabrication.
        rule.Awarded.ShouldBe(FitScorer.ValueMax / 2);
        breakdown.Warnings.ShouldContain(w => w.Contains("no contract value"));
    }

    [Fact]
    public void A_withheld_value_is_reported_differently_from_an_absent_one()
    {
        var notice = Notice(value: null);
        notice.EstimatedValueWithheld = true;

        Scorer().Score(notice, ItConsultancy()).Warnings.ShouldContain(w => w.Contains("withheld"));
    }

    [Fact]
    public void A_value_range_is_scored_on_its_midpoint()
    {
        var notice = Notice(value: null);
        notice.EstimatedValueMin = 400_000;
        notice.EstimatedValueMax = 600_000;

        var rule = Scorer().Score(notice, ItConsultancy()).Rules.Single(r => r.Rule == "value_band");

        rule.Awarded.ShouldBe(FitScorer.ValueMax);
        rule.Detail.ShouldContain("within");
    }

    [Fact]
    public void A_contract_far_above_capacity_scores_zero_on_value_but_a_near_miss_still_scores()
    {
        var farAbove = Scorer().Score(Notice(value: 50_000_000), ItConsultancy());
        var nearAbove = Scorer().Score(Notice(value: 3_000_000), ItConsultancy());

        int Value(ScoreBreakdown b) => b.Rules.Single(r => r.Rule == "value_band").Awarded;

        Value(farAbove).ShouldBe(0);
        // 1.5x the ceiling may be deliverable with a partner; 25x is not.
        Value(nearAbove).ShouldBe(FitScorer.ValueMax / 2);
    }

    [Fact]
    public void Shorter_deadlines_score_lower_and_a_very_short_one_warns()
    {
        int Deadline(int days) => Scorer()
            .Score(Notice(deadline: Now.AddDays(days)), ItConsultancy())
            .Rules.Single(r => r.Rule == "deadline_headroom").Awarded;

        Deadline(40).ShouldBe(FitScorer.DeadlineMax);
        Deadline(20).ShouldBeLessThan(Deadline(40));
        Deadline(3).ShouldBeLessThan(Deadline(20));

        Scorer().Score(Notice(deadline: Now.AddDays(3)), ItConsultancy())
            .Warnings.ShouldContain(w => w.Contains("day(s) left"));
    }

    [Fact]
    public void A_region_outside_the_profile_costs_points_without_blocking()
    {
        var breakdown = Scorer().Score(Notice(region: ["FI1D3"]), ItConsultancy());
        var rule = breakdown.Rules.Single(r => r.Rule == "region_match");

        // Same country, different area: a capable supplier can travel, so this is a discount not a gate.
        rule.Awarded.ShouldBeInRange(1, FitScorer.RegionMax - 1);
        breakdown.IsBlocked.ShouldBeFalse();
    }

    [Fact]
    public void Scoring_is_a_pure_function_of_its_inputs()
    {
        var notice = Notice();
        var profile = ItConsultancy();

        var first = Scorer().Score(notice, profile);
        var second = Scorer().Score(notice, profile);

        // The whole audit story depends on this: same inputs, same score, replayable months later.
        second.Total.ShouldBe(first.Total);
        second.Rules.Select(r => (r.Rule, r.Awarded)).ShouldBe(first.Rules.Select(r => (r.Rule, r.Awarded)));
    }

    [Fact]
    public void Every_rule_is_reported_even_when_it_scores_nothing()
    {
        var breakdown = Scorer().Score(Notice(cpv: ["45233220"], region: ["SE110"]), ItConsultancy());

        // An audit trail that omits the rules you failed is not an audit trail.
        breakdown.Rules.Select(r => r.Rule).ShouldBe(
            ["cpv_overlap", "region_match", "value_band", "deadline_headroom"], ignoreOrder: true);
        breakdown.Rules.ShouldContain(r => r.Awarded == 0);
    }

    [Theory]
    [InlineData(100, Recommendation.Go)]
    [InlineData(65, Recommendation.Go)]
    [InlineData(64, Recommendation.Investigate)]
    [InlineData(40, Recommendation.Investigate)]
    [InlineData(39, Recommendation.NoGo)]
    public void Score_thresholds_map_to_recommendations(int total, string expected)
    {
        var breakdown = new ScoreBreakdown(total, [], [], []);

        Recommendation.FromScore(breakdown).ShouldBe(expected);
    }
}
