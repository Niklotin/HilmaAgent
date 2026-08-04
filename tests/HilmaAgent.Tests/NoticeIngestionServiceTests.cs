using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using HilmaAgent.Infrastructure.Ingestion;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;

namespace HilmaAgent.Tests;

/// <summary>
/// Regression cover for the id-space collision: <c>noticeId</c> is not unique across the eForms and
/// legacy families, so it must never be used as a key or as a detail-fetch argument for eForms hits.
/// </summary>
public class NoticeIngestionServiceTests
{
    private sealed class FakeHilmaClient(params HilmaNoticeRef[] hits) : IHilmaClient
    {
        public List<string> DetailRequests { get; } = [];

        public async IAsyncEnumerable<HilmaNoticeRef> SearchAsync(DateTimeOffset publishedAfter,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var hit in hits) yield return hit;
            await Task.CompletedTask;
        }

        public Task<HilmaNoticeDocument?> GetNoticeAsync(string noticeId, CancellationToken ct = default)
        {
            DetailRequests.Add(noticeId);
            // Stands in for the 2020 legacy notice that shares the number with a 2026 eForms one.
            var raw = $$"""{"id":{{noticeId}},"type":200,"project":{"title":"Lentokonehalli"},"datePublished":"2020-09-30T17:35:03Z"}""";
            return Task.FromResult<HilmaNoticeDocument?>(new HilmaNoticeDocument(noticeId, raw, DateTimeOffset.UnixEpoch));
        }
    }

    private static HilmaDbContext InMemoryDb() => new(new DbContextOptionsBuilder<HilmaDbContext>()
        .UseInMemoryDatabase($"ingestion-{Guid.NewGuid()}")
        .Options);

    private static HilmaNoticeRef Hit(string searchId, int noticeId, bool isEForms) =>
        new(searchId, noticeId, isEForms, new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero), "{}");

    [Fact]
    public async Task Never_requests_detail_for_an_eforms_hit()
    {
        var client = new FakeHilmaClient(Hit("EF-52994", 52994, isEForms: true));
        await using var db = InMemoryDb();

        var result = await new NoticeIngestionService(client, new NoticeContractParser(), db,
            NullLogger<NoticeIngestionService>.Instance).RunAsync(10);

        // Asking for 52994 would return the unrelated 2020 legacy notice, not this one.
        client.DetailRequests.ShouldBeEmpty();
        result.UnavailableEForms.ShouldBe(1);
        result.Stored.ShouldBe(0);
        db.Notices.ShouldBeEmpty();
    }

    [Fact]
    public async Task Stores_a_legacy_notice_under_its_search_id_not_its_notice_id()
    {
        var client = new FakeHilmaClient(Hit("OLD-52994", 52994, isEForms: false));
        await using var db = InMemoryDb();

        var result = await new NoticeIngestionService(client, new NoticeContractParser(), db,
            NullLogger<NoticeIngestionService>.Instance).RunAsync(10);

        result.Stored.ShouldBe(1);
        client.DetailRequests.ShouldBe(["52994"]);

        var stored = await db.Notices.SingleAsync();
        stored.Id.ShouldBe("OLD-52994");
        stored.Title.ShouldBe("Lentokonehalli");
    }

    [Fact]
    public async Task Colliding_ids_from_both_families_do_not_overwrite_each_other()
    {
        var client = new FakeHilmaClient(
            Hit("EF-52994", 52994, isEForms: true),
            Hit("OLD-52994", 52994, isEForms: false));
        await using var db = InMemoryDb();

        var result = await new NoticeIngestionService(client, new NoticeContractParser(), db,
            NullLogger<NoticeIngestionService>.Instance).RunAsync(10);

        result.Stored.ShouldBe(1);
        result.UnavailableEForms.ShouldBe(1);
        (await db.Notices.SingleAsync()).Id.ShouldBe("OLD-52994");
    }
}
