using HilmaAgent.Core.Assessments;
using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Profiles;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace HilmaAgent.Tests;

/// <summary>
/// The API hosted against a throwaway Postgres.
/// </summary>
/// <remarks>
/// <para>The Playwright suite is deliberately read-only: decisions are append-only, so a browser test
/// that recorded one could not clean up after itself and every run would leave real rows in the audit
/// trail of the running stack. That left the <em>write</em> path — the point of the whole
/// project — covered only by hand.</para>
/// <para>A disposable database is what closes that. The container is created per test class, migrated
/// from the real migrations, and thrown away afterwards, so these tests can record decisions freely
/// and assert on append-only behaviour against a real Postgres rather than an in-memory stand-in that
/// does not enforce it.</para>
/// </remarks>
public sealed class ApiFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17-alpine")
        .WithDatabase("hilma")
        .WithUsername("hilma")
        .WithPassword("hilma")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // The worker migrates in production; nothing else does, so the fixture must.
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<HilmaDbContext>().Database.MigrateAsync();
    }

    // Explicit: xUnit's IAsyncLifetime wants Task, WebApplicationFactory's own DisposeAsync returns
    // ValueTask, and the two would otherwise collide on the same name.
    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Postgres", _postgres.GetConnectionString());

        // No Hilma, Azure or Gemini key: nothing under test here reaches an external provider, and a
        // test that silently started spending money would be a bad test.
        builder.UseSetting("Embeddings:Provider", "fake");
        builder.UseSetting("Gemini:ApiKey", "");
    }

    /// <summary>Puts one notice, profile and assessment in the database and returns the assessment.</summary>
    public async Task<FitAssessment> SeedAssessmentAsync(
        string noticeId = "EF-TEST-1",
        int score = 87,
        string scoreRecommendation = Recommendation.Go,
        string modelRecommendation = Recommendation.Investigate,
        DateTimeOffset? deadline = null)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HilmaDbContext>();

        var profile = await db.CompanyProfiles.FirstOrDefaultAsync();
        if (profile is null)
        {
            profile = new CompanyProfile
            {
                Id = Guid.NewGuid(),
                Name = "Testi Oy",
                PreferredCpvCodes = ["72000000"],
                Regions = ["FI1B"],
                CreatedAt = DateTimeOffset.UtcNow,
            };
            db.CompanyProfiles.Add(profile);
        }

        db.Notices.Add(new Notice
        {
            Id = noticeId,
            Title = "Ohjelmistokehityksen puitejärjestely",
            BuyerName = "Testivirasto",
            NoticeType = "ContractNotices",
            CpvCodes = ["72000000"],
            Region = ["FI1B1"],
            SubmissionDeadline = deadline ?? DateTimeOffset.UtcNow.AddDays(20),
            Source = NoticeSource.EForms,
            RawPayload = "{}",
            FetchedAt = DateTimeOffset.UtcNow,
        });

        var assessment = new FitAssessment
        {
            Id = Guid.NewGuid(),
            NoticeId = noticeId,
            ProfileId = profile.Id,
            DeterministicScore = score,
            ScoreBreakdownJson = """{"total":87,"rules":[],"gates":[],"warnings":[]}""",
            ScoreRecommendation = scoreRecommendation,
            ModelRecommendation = modelRecommendation,
            RecommendationDisagreement = scoreRecommendation != modelRecommendation,
            Reasoning = "Testiperustelu.",
            ModelId = "test-model",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        db.FitAssessments.Add(assessment);
        await db.SaveChangesAsync();

        return assessment;
    }
}
