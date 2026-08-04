using System.Runtime.CompilerServices;
using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using HilmaAgent.Infrastructure.Ingestion;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Shouldly;

namespace HilmaAgent.Tests;

public class NoticeIngestionServiceTests
{
    private sealed class FakeHilmaClient(params HilmaNoticeRef[] hits) : IHilmaClient
    {
        public List<string> DetailRequests { get; } = [];
        public List<IReadOnlyCollection<int>> EFormsBatches { get; } = [];
        public HashSet<int> WithoutEFormsContent { get; } = [];

        public async IAsyncEnumerable<HilmaNoticeRef> SearchAsync(
            DateTimeOffset publishedAfter,
            [EnumeratorCancellation] CancellationToken ct = default)
        {
            foreach (var hit in hits) yield return hit;
            await Task.CompletedTask;
        }

        public Task<HilmaNoticeDocument?> GetNoticeAsync(string noticeId, CancellationToken ct = default)
        {
            DetailRequests.Add(noticeId);
            // Stands in for the 2020 legacy notice that shares its number with a 2026 eForms one.
            var raw = $$"""{"id":{{noticeId}},"type":200,"project":{"title":"Lentokonehalli"},"datePublished":"2020-09-30T17:35:03Z"}""";
            return Task.FromResult<HilmaNoticeDocument?>(new HilmaNoticeDocument(noticeId, raw, DateTimeOffset.UnixEpoch));
        }

        public Task<IReadOnlyList<HilmaEFormsDocument>> GetEFormsNoticesAsync(
            IReadOnlyCollection<int> noticeIds,
            CancellationToken ct = default)
        {
            EFormsBatches.Add(noticeIds.ToList());
            IReadOnlyList<HilmaEFormsDocument> documents = noticeIds
                .Where(id => !WithoutEFormsContent.Contains(id))
                .Select(id => new HilmaEFormsDocument(id, $"<ContractNotice id=\"{id}\" />", "{}", DateTimeOffset.UnixEpoch))
                .ToList();
            return Task.FromResult(documents);
        }
    }

    private static HilmaDbContext InMemoryDb() => new(new DbContextOptionsBuilder<HilmaDbContext>()
        .UseInMemoryDatabase($"ingestion-{Guid.NewGuid()}")
        .Options);

    private static NoticeIngestionService Service(FakeHilmaClient client, HilmaDbContext db, int batchSize = 50) =>
        new(client, new NoticeContractParser(), new SearchIndexNoticeParser(), db,
            Options.Create(new HilmaOptions { EFormsBatchSize = batchSize }),
            NullLogger<NoticeIngestionService>.Instance);

    private static HilmaNoticeRef Hit(string searchId, int noticeId, bool isEForms, string? titleFi = null) =>
        new(searchId, noticeId, isEForms, new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero),
            $$"""
            {"id":"{{searchId}}","noticeId":{{noticeId}},"isEForms":{{(isEForms ? "true" : "false")}},
             "titleFi":"{{titleFi ?? "Testihankinta"}}","mainType":"ContractNotices","type":"16",
             "cpvCodes":"72000000 72200000","nutsCodes":"FI1B1","organisationNameFi":"Helsingin kaupunki",
             "estimatedValue":450000,"currency":"EUR","deadline":"2026-09-01T12:00:00Z"}
            """);

    [Fact]
    public async Task Never_requests_legacy_detail_for_an_eforms_hit()
    {
        var client = new FakeHilmaClient(Hit("EF-52994", 52994, isEForms: true));
        await using var db = InMemoryDb();

        await Service(client, db).RunAsync(10);

        // Asking the legacy API for 52994 would return the unrelated 2020 notice.
        client.DetailRequests.ShouldBeEmpty();
        client.EFormsBatches.Single().ShouldBe([52994]);
    }

    [Fact]
    public async Task Stores_a_legacy_notice_under_its_search_id_not_its_notice_id()
    {
        var client = new FakeHilmaClient(Hit("OLD-52994", 52994, isEForms: false));
        await using var db = InMemoryDb();

        var result = await Service(client, db).RunAsync(10);

        result.Stored.ShouldBe(1);
        client.DetailRequests.ShouldBe(["52994"]);

        var stored = await db.Notices.SingleAsync();
        stored.Id.ShouldBe("OLD-52994");
        stored.Source.ShouldBe(NoticeSource.Legacy);
        stored.Title.ShouldBe("Lentokonehalli");
    }

    [Fact]
    public async Task Colliding_ids_from_both_families_are_stored_side_by_side()
    {
        var client = new FakeHilmaClient(
            Hit("EF-52994", 52994, isEForms: true),
            Hit("OLD-52994", 52994, isEForms: false));
        await using var db = InMemoryDb();

        var result = await Service(client, db).RunAsync(10);

        result.Stored.ShouldBe(1);
        result.StoredEForms.ShouldBe(1);
        (await db.Notices.CountAsync()).ShouldBe(2);
        (await db.Notices.Select(n => n.Id).ToListAsync()).ShouldBe(["OLD-52994", "EF-52994"], ignoreOrder: true);
    }

    [Fact]
    public async Task Eforms_notices_take_structured_fields_from_the_index_and_keep_the_xml()
    {
        var client = new FakeHilmaClient(Hit("EF-1", 1, isEForms: true, titleFi: "Ohjelmistokehityksen palvelut"));
        await using var db = InMemoryDb();

        await Service(client, db).RunAsync(10);

        var stored = await db.Notices.SingleAsync();
        stored.Source.ShouldBe(NoticeSource.EForms);
        stored.Title.ShouldBe("Ohjelmistokehityksen palvelut");
        stored.BuyerName.ShouldBe("Helsingin kaupunki");
        stored.CpvCodes.ShouldBe(["72000000", "72200000"]);
        stored.Region.ShouldBe(["FI1B1"]);
        stored.EstimatedValue.ShouldBe(450000m);
        stored.SubmissionDeadline.ShouldBe(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        stored.EFormsXml.ShouldContain("ContractNotice");
    }

    [Fact]
    public async Task Eforms_fetches_are_batched_rather_than_one_call_per_notice()
    {
        var hits = Enumerable.Range(1, 5).Select(i => Hit($"EF-{i}", i, isEForms: true)).ToArray();
        var client = new FakeHilmaClient(hits);
        await using var db = InMemoryDb();

        await Service(client, db, batchSize: 2).RunAsync(10);

        // 5 notices at a batch size of 2 => 2 full batches plus a final flush, not 5 calls.
        client.EFormsBatches.Count.ShouldBe(3);
        client.EFormsBatches.Select(batch => batch.Count).ShouldBe([2, 2, 1]);
        (await db.Notices.CountAsync()).ShouldBe(5);
    }

    [Fact]
    public async Task A_notice_whose_xml_is_missing_is_still_stored_with_its_index_fields()
    {
        var client = new FakeHilmaClient(Hit("EF-7", 7, isEForms: true));
        client.WithoutEFormsContent.Add(7);
        await using var db = InMemoryDb();

        var result = await Service(client, db).RunAsync(10);

        result.StoredEForms.ShouldBe(1);
        result.MissingContent.ShouldBe(1);

        var stored = await db.Notices.SingleAsync();
        stored.EFormsXml.ShouldBeNull();
        stored.Title.ShouldNotBeNull();
    }

    [Fact]
    public async Task Already_ingested_notices_are_skipped_without_refetching()
    {
        var client = new FakeHilmaClient(Hit("OLD-5", 5, isEForms: false));
        await using var db = InMemoryDb();

        await Service(client, db).RunAsync(10);
        var second = await Service(new FakeHilmaClient(Hit("OLD-5", 5, isEForms: false)), db).RunAsync(10);

        second.Skipped.ShouldBe(1);
        second.Stored.ShouldBe(0);
    }
}
