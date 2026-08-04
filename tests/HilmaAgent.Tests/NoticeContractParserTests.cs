using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using Shouldly;

namespace HilmaAgent.Tests;

public class NoticeContractParserTests
{
    private readonly NoticeContractParser _parser = new();

    private Notice ParseExample() => _parser.Parse(
        new HilmaNoticeDocument("0", Fixture.Read("notice-contract-example.json"), DateTimeOffset.UnixEpoch));

    [Fact]
    public void Maps_the_published_contract_example()
    {
        var notice = ParseExample();

        notice.Id.ShouldBe("0");
        notice.Title.ShouldBe("Cars for the London office");
        notice.BuyerName.ShouldBe("Innofactor Oyj");
        notice.BuyerNationalRegistrationNumber.ShouldBe("0686163-7");
        notice.Language.ShouldBe("EN");
        notice.SubmissionDeadline.ShouldBe(new DateTimeOffset(2020, 2, 1, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void Resolves_integer_enums_to_names()
    {
        var notice = ParseExample();

        notice.NoticeTypeCode.ShouldBe(200);
        notice.NoticeType.ShouldBe("Contract");
        // contractingAuthorityType 2 — the API models these as ints, unreadable without the map.
        notice.BuyerOrganizationType.ShouldBe("MaintypeNatagency");
    }

    [Fact]
    public void Unions_cpv_codes_across_the_notice_and_its_lots()
    {
        var notice = ParseExample();

        notice.CpvCodes.ShouldContain("34110000");
        // A lot-level code is still a code the buyer wants, and duplicates collapse.
        notice.CpvCodes.Distinct().Count().ShouldBe(notice.CpvCodes.Count);
    }

    [Fact]
    public void Reads_nuts_codes_from_lots()
    {
        ParseExample().Region.ShouldContain("UKI32");
    }

    [Fact]
    public void Reads_an_exact_estimated_value_with_its_currency()
    {
        var notice = ParseExample();

        notice.EstimatedValue.ShouldBe(120003.5m);
        notice.Currency.ShouldBe("EUR");
        notice.EstimatedValueWithheld.ShouldBeFalse();
    }

    [Fact]
    public void Keeps_a_value_range_as_a_band_rather_than_collapsing_it()
    {
        // ValueRangeContract type 2 = Range: min/max instead of an exact figure.
        const string raw = """
        {"id":7,"procurementObject":{"estimatedValue":{"type":2,"minValue":50000,"maxValue":250000,"currency":"EUR"}}}
        """;

        var notice = _parser.Parse(new HilmaNoticeDocument("7", raw, DateTimeOffset.UnixEpoch));

        notice.EstimatedValue.ShouldBeNull();
        notice.EstimatedValueMin.ShouldBe(50000m);
        notice.EstimatedValueMax.ShouldBe(250000m);
    }

    [Fact]
    public void Distinguishes_a_withheld_value_from_a_missing_one()
    {
        var withheld = _parser.Parse(new HilmaNoticeDocument("8",
            """{"id":8,"procurementObject":{"estimatedValue":{"type":0,"disagreeToBePublished":true}}}""",
            DateTimeOffset.UnixEpoch));

        var absent = _parser.Parse(new HilmaNoticeDocument("9", """{"id":9}""", DateTimeOffset.UnixEpoch));

        withheld.EstimatedValue.ShouldBeNull();
        withheld.EstimatedValueWithheld.ShouldBeTrue();

        absent.EstimatedValue.ShouldBeNull();
        absent.EstimatedValueWithheld.ShouldBeFalse();
    }

    [Fact]
    public void Sparse_notice_parses_without_throwing_and_keeps_the_raw_payload()
    {
        const string raw = """{"id":123,"type":9902}""";

        var notice = _parser.Parse(new HilmaNoticeDocument("123", raw, DateTimeOffset.UnixEpoch));

        notice.Id.ShouldBe("123");
        notice.NoticeType.ShouldBe("NationalContract");
        notice.Title.ShouldBeNull();
        notice.CpvCodes.ShouldBeEmpty();
        notice.Region.ShouldBeEmpty();
        notice.PublicationDate.ShouldBeNull();
        notice.RawPayload.ShouldBe(raw);
    }

    [Fact]
    public void Unknown_notice_type_keeps_the_code_and_leaves_the_name_null()
    {
        // New notice types will appear before we regenerate HilmaEnums; the code must survive.
        var notice = _parser.Parse(new HilmaNoticeDocument("1", """{"id":1,"type":424242}""", DateTimeOffset.UnixEpoch));

        notice.NoticeTypeCode.ShouldBe(424242);
        notice.NoticeType.ShouldBeNull();
    }
}

internal static class Fixture
{
    public static string Read(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
