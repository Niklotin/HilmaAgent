using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using HilmaAgent.Infrastructure.Assessments;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Shouldly;

namespace HilmaAgent.Tests;

public class AssessmentServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 4, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Returns whatever it is told to, so the service's handling of model output can be tested.</summary>
    private sealed class ScriptedNarrator(string recommendation, IReadOnlyList<Guid>? citations = null) : IAssessmentNarrator
    {
        public string ModelId => "scripted-test-model";

        public IReadOnlyList<NarrationSource> Received { get; private set; } = [];
        public ScoreBreakdown? ReceivedBreakdown { get; private set; }

        public Task<Narration> NarrateAsync(
            Notice notice, CompanyProfile profile, ScoreBreakdown breakdown,
            IReadOnlyList<NarrationSource> sources, CancellationToken ct = default)
        {
            Received = sources;
            ReceivedBreakdown = breakdown;
            return Task.FromResult(new Narration(recommendation, "Perustelu.", citations ?? []));
        }
    }

    private static HilmaDbContext InMemoryDb() => new(new DbContextOptionsBuilder<HilmaDbContext>()
        .UseInMemoryDatabase($"assess-{Guid.NewGuid()}")
        .Options);

    private static async Task<(HilmaDbContext Db, Guid ChunkId)> SeedAsync(
        HilmaDbContext db, string? noticeType = "ContractNotices")
    {
        var profile = SeedProfileFor();
        db.CompanyProfiles.Add(profile);

        db.Notices.Add(new Notice
        {
            Id = "EF-1",
            Title = "Ohjelmistokehityksen puitesopimus",
            NoticeType = noticeType,
            CpvCodes = ["72200000"],
            Region = ["FI1B1"],
            EstimatedValue = 500_000,
            SubmissionDeadline = Now.AddDays(30),
            Source = NoticeSource.EForms,
            RawPayload = "{}",
        });

        var chunkId = Guid.NewGuid();
        db.NoticeChunks.Add(new NoticeChunk
        {
            Id = chunkId,
            VectorId = chunkId,
            NoticeId = "EF-1",
            Section = ChunkSection.Summary,
            ChunkIndex = 0,
            Content = "Hankinta: Ohjelmistokehityksen puitesopimus",
        });

        await db.SaveChangesAsync();
        return (db, chunkId);
    }

    private static CompanyProfile SeedProfileFor() => new()
    {
        Id = Guid.NewGuid(),
        Name = "Testi Oy",
        PreferredCpvCodes = ["72000000"],
        Regions = ["FI1B"],
        MinContractValue = 50_000,
        MaxContractValue = 3_000_000,
        CreatedAt = Now,
    };

    private static AssessmentService Service(HilmaDbContext db, IAssessmentNarrator narrator) =>
        new(db, new FitScorer(new FakeTimeProvider(Now)), new FixedNarrator(narrator), NullLogger<AssessmentService>.Instance);

    /// <summary>Hands back one narrator, so these tests need no provider, key or database lookup.</summary>
    private sealed class FixedNarrator(IAssessmentNarrator narrator) : INarratorRegistry
    {
        public Task<IAssessmentNarrator> ResolveAsync(CancellationToken ct = default) => Task.FromResult(narrator);
    }

    [Fact]
    public async Task Invented_citations_are_dropped_rather_than_stored()
    {
        await using var db = InMemoryDb();
        var (_, realChunk) = await SeedAsync(db);

        var narrator = new ScriptedNarrator(Recommendation.Go, [realChunk, Guid.NewGuid(), Guid.NewGuid()]);
        var assessment = await Service(db, narrator).AssessAsync("EF-1");

        // A citation pointing at a chunk the model was never given is unverifiable, and an
        // unverifiable citation is worse than none because it looks like evidence.
        assessment.Citations.Count.ShouldBe(1);
        assessment.Citations[0].ChunkId.ShouldBe(realChunk);
    }

    [Fact]
    public async Task A_model_recommendation_differing_from_the_score_is_flagged_not_overwritten()
    {
        await using var db = InMemoryDb();
        await SeedAsync(db);

        // The score on this notice implies GO; the model says otherwise.
        var assessment = await Service(db, new ScriptedNarrator(Recommendation.Investigate)).AssessAsync("EF-1");

        assessment.ScoreRecommendation.ShouldBe(Recommendation.Go);
        assessment.ModelRecommendation.ShouldBe(Recommendation.Investigate);
        assessment.RecommendationDisagreement.ShouldBeTrue();
        // Both survive: silently trusting either one would discard the signal.
        assessment.DeterministicScore.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Agreement_is_not_flagged()
    {
        await using var db = InMemoryDb();
        await SeedAsync(db);

        var assessment = await Service(db, new ScriptedNarrator(Recommendation.Go)).AssessAsync("EF-1");

        assessment.RecommendationDisagreement.ShouldBeFalse();
    }

    [Fact]
    public async Task The_model_receives_a_finished_score_and_cannot_change_it()
    {
        await using var db = InMemoryDb();
        await SeedAsync(db);

        var narrator = new ScriptedNarrator(Recommendation.NoGo);
        var assessment = await Service(db, narrator).AssessAsync("EF-1");

        // Whatever the model says, the stored number is the one computed before it was called.
        narrator.ReceivedBreakdown.ShouldNotBeNull();
        assessment.DeterministicScore.ShouldBe(narrator.ReceivedBreakdown!.Total);
        assessment.ModelRecommendation.ShouldBe(Recommendation.NoGo);
    }

    [Fact]
    public async Task A_gated_notice_still_gets_assessed_and_records_the_gate()
    {
        await using var db = InMemoryDb();
        await SeedAsync(db, noticeType: "ContractAwardNotices");

        var assessment = await Service(db, new ScriptedNarrator(Recommendation.NoGo)).AssessAsync("EF-1");

        assessment.DeterministicScore.ShouldBe(0);
        assessment.ScoreRecommendation.ShouldBe(Recommendation.NoGo);
        assessment.ScoreBreakdownJson.ShouldContain("not_biddable");
    }

    [Fact]
    public async Task The_model_only_ever_sees_the_notice_it_is_assessing()
    {
        await using var db = InMemoryDb();
        var (_, chunkId) = await SeedAsync(db);

        // A chunk belonging to a different notice must not leak into the sources.
        var otherId = Guid.NewGuid();
        db.Notices.Add(new Notice { Id = "EF-2", Source = NoticeSource.EForms, RawPayload = "{}" });
        db.NoticeChunks.Add(new NoticeChunk
        {
            Id = otherId, VectorId = otherId, NoticeId = "EF-2",
            Section = ChunkSection.Summary, ChunkIndex = 0, Content = "Toinen ilmoitus",
        });
        await db.SaveChangesAsync();

        var narrator = new ScriptedNarrator(Recommendation.Go);
        await Service(db, narrator).AssessAsync("EF-1");

        narrator.Received.Select(source => source.ChunkId).ShouldBe([chunkId]);
    }

    [Fact]
    public async Task Progress_reports_the_score_before_the_model_is_called()
    {
        await using var db = InMemoryDb();
        await SeedAsync(db);

        var steps = new List<string>();
        var progress = new Progress<AssessmentProgress>(p => steps.Add(p.Step));

        await Service(db, new ScriptedNarrator(Recommendation.Go)).AssessAsync("EF-1", progress: progress);

        // Progress is reported synchronously enough for ordering to be meaningful here; the
        // guarantee under test is that scoring precedes narration in the pipeline.
        await Task.Delay(50);
        steps.IndexOf("scoring").ShouldBeLessThan(steps.IndexOf("narrating"));
    }

    [Fact]
    public async Task An_unknown_notice_is_reported_as_missing_rather_than_assessed()
    {
        await using var db = InMemoryDb();
        await SeedAsync(db);

        await Should.ThrowAsync<KeyNotFoundException>(
            () => Service(db, new ScriptedNarrator(Recommendation.Go)).AssessAsync("EF-does-not-exist"));
    }
}
