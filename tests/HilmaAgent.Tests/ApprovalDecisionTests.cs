using HilmaAgent.Core.Assessments;
using Shouldly;

namespace HilmaAgent.Tests;

public class ApprovalDecisionTests
{
    [Fact]
    public void An_approved_decision_stands_behind_the_agents_recommendation()
    {
        var decision = new ApprovalDecision { Decision = DecisionType.Approved };

        decision.EffectiveRecommendation(Recommendation.Go).ShouldBe(Recommendation.Go);
    }

    [Fact]
    public void An_edited_decision_replaces_the_recommendation_without_erasing_the_original()
    {
        var decision = new ApprovalDecision
        {
            Decision = DecisionType.Edited,
            EditedRecommendation = Recommendation.NoGo,
        };

        // The agent said GO; the reviewer overrode it. Both remain readable — the assessment keeps
        // its own recommendation, and this row records what the human actually stood behind.
        decision.EffectiveRecommendation(Recommendation.Go).ShouldBe(Recommendation.NoGo);
        decision.EditedRecommendation.ShouldBe(Recommendation.NoGo);
    }

    [Fact]
    public void A_rejection_leaves_the_agents_recommendation_as_the_thing_being_rejected()
    {
        var decision = new ApprovalDecision { Decision = DecisionType.Rejected };

        // REJECTED is not a counter-proposal: it means "not this", and the shortlist simply
        // excludes it. Reading an edit out of a rejection would invent an opinion nobody expressed.
        decision.EffectiveRecommendation(Recommendation.Go).ShouldBe(Recommendation.Go);
    }

    [Theory]
    [InlineData("APPROVED", true)]
    [InlineData("REJECTED", true)]
    [InlineData("EDITED", true)]
    [InlineData("approved", false)]
    [InlineData("MAYBE", false)]
    [InlineData(null, false)]
    public void Decision_types_are_validated(string? value, bool expected) =>
        DecisionType.IsValid(value).ShouldBe(expected);
}
