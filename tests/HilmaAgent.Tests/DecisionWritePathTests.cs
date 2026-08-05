using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HilmaAgent.Core.Assessments;
using Shouldly;

namespace HilmaAgent.Tests;

/// <summary>
/// Recording decisions, against a real database that is thrown away afterwards.
/// </summary>
/// <remarks>
/// This is the path the browser suite cannot take. Everything here writes rows, and the whole point
/// of the audit trail is that written rows stay written — so it needs a database nobody minds
/// losing rather than the one the running stack uses.
/// </remarks>
public class DecisionWritePathTests(ApiFixture api) : IClassFixture<ApiFixture>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private HttpClient Client => api.CreateClient();

    private static object Decision(string decision, string? edited = null, string? note = null, string? by = "Niko") =>
        new { decision, editedRecommendation = edited, reviewerNote = note, reviewedBy = by };

    [Fact]
    public async Task A_decision_is_recorded_and_leaves_the_queue()
    {
        var assessment = await api.SeedAssessmentAsync("EF-WRITE-1");
        var client = Client;

        (await client.GetFromJsonAsync<List<JsonElement>>("/api/queue", Json))!
            .ShouldContain(item => item.GetProperty("id").GetGuid() == assessment.Id);

        var response = await client.PostAsJsonAsync(
            $"/api/assessments/{assessment.Id}/decision",
            Decision(DecisionType.Approved, note: "Sopii profiiliin."));

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        (await client.GetFromJsonAsync<List<JsonElement>>("/api/queue", Json))!
            .ShouldNotContain(item => item.GetProperty("id").GetGuid() == assessment.Id);
    }

    [Fact]
    public async Task A_decided_assessment_appears_with_its_note_and_author()
    {
        var assessment = await api.SeedAssessmentAsync("EF-WRITE-2");
        var client = Client;

        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision",
            Decision(DecisionType.Approved, note: "Sopii profiiliin.", by: "Niko"));

        var decided = (await client.GetFromJsonAsync<List<JsonElement>>("/api/decided", Json))!
            .Single(d => d.GetProperty("id").GetGuid() == assessment.Id);

        decided.GetProperty("decision").GetString().ShouldBe(DecisionType.Approved);
        decided.GetProperty("reviewerNote").GetString().ShouldBe("Sopii profiiliin.");
        decided.GetProperty("reviewedBy").GetString().ShouldBe("Niko");
        decided.GetProperty("revisionCount").GetInt32().ShouldBe(1);
    }

    /// <summary>
    /// Changing your mind writes a new row and keeps the old one.
    /// </summary>
    /// <remarks>
    /// The claim the project makes about its own audit trail. An in-memory database would let this
    /// pass without ever proving it, which is why it runs against real Postgres.
    /// </remarks>
    [Fact]
    public async Task Changing_your_mind_appends_rather_than_overwrites()
    {
        var assessment = await api.SeedAssessmentAsync("EF-WRITE-3");
        var client = Client;

        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision",
            Decision(DecisionType.Rejected, note: "Ei kuulu profiiliin."));

        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision",
            Decision(DecisionType.Edited, edited: Recommendation.NoGo, note: "Tarkennus."));

        var history = await client.GetFromJsonAsync<List<JsonElement>>(
            $"/api/assessments/{assessment.Id}/decisions", Json);

        history!.Count.ShouldBe(2);
        // Newest first, and the earlier verdict is still readable underneath it.
        history[0].GetProperty("decision").GetString().ShouldBe(DecisionType.Edited);
        history[1].GetProperty("decision").GetString().ShouldBe(DecisionType.Rejected);
        history[1].GetProperty("reviewerNote").GetString().ShouldBe("Ei kuulu profiiliin.");

        var decided = (await client.GetFromJsonAsync<List<JsonElement>>("/api/decided", Json))!
            .Single(d => d.GetProperty("id").GetGuid() == assessment.Id);

        // Only the latest counts as the verdict; the count says the mind was changed.
        decided.GetProperty("decision").GetString().ShouldBe(DecisionType.Edited);
        decided.GetProperty("revisionCount").GetInt32().ShouldBe(2);
        decided.GetProperty("effectiveRecommendation").GetString().ShouldBe(Recommendation.NoGo);
    }

    [Fact]
    public async Task An_edit_replaces_the_recommendation_without_erasing_the_model_s()
    {
        var assessment = await api.SeedAssessmentAsync("EF-WRITE-4",
            modelRecommendation: Recommendation.Investigate);
        var client = Client;

        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision",
            Decision(DecisionType.Edited, edited: Recommendation.Go));

        var decided = (await client.GetFromJsonAsync<List<JsonElement>>("/api/decided", Json))!
            .Single(d => d.GetProperty("id").GetGuid() == assessment.Id);

        decided.GetProperty("effectiveRecommendation").GetString().ShouldBe(Recommendation.Go);
        // The model's own answer survives beside it, which is what keeps the disagreement visible.
        decided.GetProperty("modelRecommendation").GetString().ShouldBe(Recommendation.Investigate);
    }

    [Fact]
    public async Task A_backed_assessment_reaches_the_shortlist_and_a_rejected_one_does_not()
    {
        var backed = await api.SeedAssessmentAsync("EF-WRITE-5", modelRecommendation: Recommendation.Go);
        var rejected = await api.SeedAssessmentAsync("EF-WRITE-6", modelRecommendation: Recommendation.Go);
        var client = Client;

        await client.PostAsJsonAsync($"/api/assessments/{backed.Id}/decision", Decision(DecisionType.Approved));
        await client.PostAsJsonAsync($"/api/assessments/{rejected.Id}/decision", Decision(DecisionType.Rejected));

        var shortlist = await client.GetFromJsonAsync<List<JsonElement>>("/api/shortlist", Json);
        var ids = shortlist!.Select(s => s.GetProperty("assessmentId").GetGuid()).ToList();

        ids.ShouldContain(backed.Id);
        ids.ShouldNotContain(rejected.Id);
    }

    [Fact]
    public async Task An_approved_no_go_is_an_agreement_not_to_bid_rather_than_a_shortlist_entry()
    {
        var assessment = await api.SeedAssessmentAsync("EF-WRITE-7", modelRecommendation: Recommendation.NoGo);
        var client = Client;

        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision", Decision(DecisionType.Approved));

        var shortlist = await client.GetFromJsonAsync<List<JsonElement>>("/api/shortlist", Json);

        shortlist!.ShouldNotContain(s => s.GetProperty("assessmentId").GetGuid() == assessment.Id);
    }

    [Fact]
    public async Task A_closed_tender_drops_off_the_shortlist_but_keeps_its_decision()
    {
        var assessment = await api.SeedAssessmentAsync("EF-WRITE-8",
            modelRecommendation: Recommendation.Go,
            deadline: DateTimeOffset.UtcNow.AddDays(-1));
        var client = Client;

        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision", Decision(DecisionType.Approved));

        (await client.GetFromJsonAsync<List<JsonElement>>("/api/shortlist", Json))!
            .ShouldNotContain(s => s.GetProperty("assessmentId").GetGuid() == assessment.Id);

        // A closed tender is not a task any more, but the decision stays readable.
        (await client.GetFromJsonAsync<List<JsonElement>>("/api/decided", Json))!
            .ShouldContain(d => d.GetProperty("id").GetGuid() == assessment.Id);
    }

    [Fact]
    public async Task Metrics_count_only_the_latest_decision_per_assessment()
    {
        var assessment = await api.SeedAssessmentAsync("EF-WRITE-9");
        var client = Client;

        var before = await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json);
        var reviewedBefore = before.GetProperty("reviewed").GetInt32();

        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision", Decision(DecisionType.Approved));
        await client.PostAsJsonAsync($"/api/assessments/{assessment.Id}/decision", Decision(DecisionType.Rejected));

        var after = await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json);

        // Two rows, one assessment: earlier decisions are history, not extra reviews.
        after.GetProperty("reviewed").GetInt32().ShouldBe(reviewedBefore + 1);
    }

    [Theory]
    [InlineData("MAYBE", null)]
    [InlineData("approved", null)]
    [InlineData("EDITED", null)]
    public async Task An_invalid_decision_is_refused(string decision, string? edited)
    {
        var assessment = await api.SeedAssessmentAsync($"EF-BAD-{decision}");

        var response = await Client.PostAsJsonAsync(
            $"/api/assessments/{assessment.Id}/decision", Decision(decision, edited));

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_decision_on_an_unknown_assessment_is_a_not_found()
    {
        var response = await Client.PostAsJsonAsync(
            $"/api/assessments/{Guid.NewGuid()}/decision", Decision(DecisionType.Approved));

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Escalation splits urgency by what it demands of the reader.
    /// </summary>
    /// <remarks>
    /// An undecided notice closing on Friday needs a decision; one already backed needs a bid. A
    /// single "5 urgent" would leave the reviewer to work out which, so they are counted separately.
    /// </remarks>
    [Fact]
    public async Task Escalation_separates_what_needs_a_decision_from_what_needs_a_bid()
    {
        var undecided = await api.SeedAssessmentAsync("EF-SOON-1", deadline: DateTimeOffset.UtcNow.AddDays(3));
        var backed = await api.SeedAssessmentAsync("EF-SOON-2",
            modelRecommendation: Recommendation.Go, deadline: DateTimeOffset.UtcNow.AddDays(4));
        var rejected = await api.SeedAssessmentAsync("EF-SOON-3",
            modelRecommendation: Recommendation.Go, deadline: DateTimeOffset.UtcNow.AddDays(5));
        var client = Client;

        var before = await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json);
        var undecidedBefore = before.GetProperty("undecidedClosingSoon").GetInt32();
        var backedBefore = before.GetProperty("backedClosingSoon").GetInt32();

        await client.PostAsJsonAsync($"/api/assessments/{backed.Id}/decision", Decision(DecisionType.Approved));
        await client.PostAsJsonAsync($"/api/assessments/{rejected.Id}/decision", Decision(DecisionType.Rejected));

        var after = await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json);

        // Deciding two of the three leaves one still awaiting a decision.
        after.GetProperty("undecidedClosingSoon").GetInt32().ShouldBe(undecidedBefore - 2);
        // Only the approved one needs a bid; the rejection is finished business.
        after.GetProperty("backedClosingSoon").GetInt32().ShouldBe(backedBefore + 1);

        undecided.ShouldNotBeNull();
    }

    [Fact]
    public async Task A_distant_deadline_is_not_an_escalation()
    {
        var client = Client;
        var before = (await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json))
            .GetProperty("undecidedClosingSoon").GetInt32();

        await api.SeedAssessmentAsync("EF-FAR-1", deadline: DateTimeOffset.UtcNow.AddDays(40));

        var after = (await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json))
            .GetProperty("undecidedClosingSoon").GetInt32();

        after.ShouldBe(before);
    }

    [Fact]
    public async Task A_closed_tender_is_not_an_escalation_either()
    {
        var client = Client;
        var before = (await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json))
            .GetProperty("undecidedClosingSoon").GetInt32();

        // Already past: it needs nothing from anyone, and nagging about it would be noise.
        await api.SeedAssessmentAsync("EF-PAST-1", deadline: DateTimeOffset.UtcNow.AddDays(-2));

        var after = (await client.GetFromJsonAsync<JsonElement>("/api/metrics", Json))
            .GetProperty("undecidedClosingSoon").GetInt32();

        after.ShouldBe(before);
    }
}
