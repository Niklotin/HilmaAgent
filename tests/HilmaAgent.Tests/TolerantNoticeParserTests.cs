using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using Shouldly;

namespace HilmaAgent.Tests;

public class TolerantNoticeParserTests
{
    private readonly TolerantNoticeParser _parser = new();

    [Fact]
    public void Parses_flat_legacy_shaped_payload()
    {
        var notice = Parse("notice-legacy.json", "2026-123456");

        notice.Title.ShouldBe("Ohjelmistokehityksen asiantuntijapalvelut");
        notice.BuyerName.ShouldBe("Helsingin kaupunki");
        notice.CpvCodes.ShouldBe(["72000000", "72200000"]);
        notice.EstimatedValue.ShouldBe(450000m);
        notice.Currency.ShouldBe("EUR");
        notice.PublicationDate.ShouldBe(new DateTimeOffset(2026, 7, 14, 9, 0, 0, TimeSpan.Zero));
        notice.SubmissionDeadline.ShouldBe(new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero));
        notice.Region.ShouldBe("FI1B1");
    }

    [Fact]
    public void Parses_nested_eforms_shaped_payload()
    {
        var notice = Parse("notice-eforms.json", "2026-999888");

        // Title arrives as a language-keyed object; the parser takes the first string value.
        notice.Title.ShouldBe("Tietoliikenneverkon uusiminen");
        notice.BuyerName.ShouldBe("Tampereen yliopisto");
        notice.CpvCodes.ShouldContain("32400000");
        notice.EstimatedValue.ShouldBe(1250000m);
        notice.Region.ShouldBe("FI197");
        notice.SubmissionDeadline!.Value.ToUniversalTime()
            .ShouldBe(new DateTimeOffset(2026, 8, 28, 7, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Unrecognised_payload_still_persists_with_raw_json_intact()
    {
        const string raw = """{"somethingCompletelyDifferent": {"nested": 1}}""";

        var notice = _parser.Parse(new HilmaNoticeDocument("x-1", raw, DateTimeOffset.UnixEpoch));

        notice.Id.ShouldBe("x-1");
        notice.Title.ShouldBeNull();
        notice.CpvCodes.ShouldBeEmpty();
        notice.RawPayload.ShouldBe(raw);
    }

    [Fact]
    public void Missing_estimated_value_stays_null_rather_than_zero()
    {
        var notice = _parser.Parse(new HilmaNoticeDocument("x-2", """{"title": "No value given"}""", DateTimeOffset.UnixEpoch));

        // Absent value and a genuine zero must stay distinguishable — Phase 3 scoring depends on it.
        notice.EstimatedValue.ShouldBeNull();
    }

    private Notice Parse(string fixtureName, string noticeId) =>
        _parser.Parse(new HilmaNoticeDocument(noticeId, Fixture.Read(fixtureName), DateTimeOffset.UtcNow));
}

internal static class Fixture
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
