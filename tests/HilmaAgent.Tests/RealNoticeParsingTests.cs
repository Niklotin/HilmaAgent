using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using Shouldly;

namespace HilmaAgent.Tests;

/// <summary>
/// The parser against a real published Finnish notice, rather than the vendor's example.
/// </summary>
/// <remarks>
/// <para><c>notice-contract-example.json</c> is authoritative on shape and synthetic in its values —
/// English text, a London NUTS code, and no <c>datePublished</c> at all. Real notices differ in ways
/// that matter: Finnish characters, national notice types, hierarchical NUTS, absent contract values,
/// and timestamps written <em>without a timezone offset</em>.</para>
/// <para>The fixture is <c>OLD-160424</c> as returned by the Read API, verbatim except for the
/// <c>contactPerson</c> block — see <c>Fixtures/README.md</c>.</para>
/// </remarks>
public class RealNoticeParsingTests
{
    private static Notice Parse() => new NoticeContractParser().Parse(
        new HilmaNoticeDocument("OLD-160424", Fixture.Read("notice-contract-real-fi.json"), DateTimeOffset.UnixEpoch));

    [Fact]
    public void Reads_a_real_finnish_notice()
    {
        var notice = Parse();

        notice.Title.ShouldBe("Kiinteistöjen esteettömyyskartoituspalvelun hankinta (15kpl)");
        notice.BuyerName.ShouldBe("Kotkan Julkiset Kiinteistöt Oy");
        notice.BuyerNationalRegistrationNumber.ShouldBe("2615544-9");
        notice.Language.ShouldBe("FI");
        notice.Source.ShouldBe(NoticeSource.Legacy);
    }

    [Fact]
    public void Resolves_a_national_notice_type_the_vendor_example_does_not_carry()
    {
        var notice = Parse();

        notice.NoticeTypeCode.ShouldBe(9912);
        notice.NoticeType.ShouldBe("NationalSmallValueProcurement");
    }

    [Fact]
    public void Collects_the_cpv_code_and_both_levels_of_nuts()
    {
        var notice = Parse();

        notice.CpvCodes.ShouldBe(["71317000"]);
        // Real notices carry the region at more than one depth; the scorer matches hierarchically,
        // so both need to survive rather than one being collapsed into the other.
        notice.Region.ShouldContain("FI1C7");
        notice.Region.ShouldContain("FI");
    }

    /// <summary>
    /// Hilma writes timestamps with no timezone offset, so parsing must pin them to UTC explicitly.
    /// </summary>
    /// <remarks>
    /// Read as local time instead, the same notice yields a different instant depending on where the
    /// ingester runs — UTC in the container, UTC+3 on a Finnish developer's machine. That would make
    /// a stored deadline a property of the machine that fetched it, which breaks replayability and,
    /// worse, moves the <c>deadline_passed</c> gate and the headroom points that depend on it.
    /// The vendor example cannot catch this: it omits <c>datePublished</c> entirely.
    /// </remarks>
    [Fact]
    public void Treats_offsetless_timestamps_as_utc_regardless_of_machine_timezone()
    {
        var notice = Parse();

        // Raw: "2026-07-15T06:43:28.5976352", no offset.
        notice.PublicationDate.ShouldBe(
            new DateTimeOffset(2026, 7, 15, 6, 43, 28, TimeSpan.Zero).AddTicks(5_976_352 % 10_000_000));

        // Raw: "2026-08-17T12:00:00", no offset. Noon UTC, not noon wherever the tests happen to run.
        notice.SubmissionDeadline.ShouldBe(new DateTimeOffset(2026, 8, 17, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public void An_absent_contract_value_is_not_a_withheld_one()
    {
        var notice = Parse();

        // Most real notices state no value at all. That is distinct from the buyer refusing to
        // publish it, and the value-band rule scores the two differently.
        notice.EstimatedValue.ShouldBeNull();
        notice.EstimatedValueMin.ShouldBeNull();
        notice.EstimatedValueMax.ShouldBeNull();
        notice.EstimatedValueWithheld.ShouldBeFalse();
    }

    [Fact]
    public void Keeps_fields_it_does_not_map_so_a_reparse_can_reach_them()
    {
        var notice = Parse();

        // Re-parsing is the migration path for every field added later, so the payload has to keep
        // what the current mapping ignores. These three are unmapped today and would be lost if the
        // parser stored only what it understood.
        notice.RawPayload.ShouldContain("hilmaSubmissionDate");
        notice.RawPayload.ShouldContain("procurementQuestionDueDate");
        notice.RawPayload.ShouldContain("lotsInfo");
    }

    [Fact]
    public void Survives_the_round_trip_through_utf8()
    {
        // The corpus is Finnish and the fixture is read from disk, so a mis-declared encoding
        // anywhere in the chain shows up here as mojibake rather than silently in a citation.
        Parse().Title.ShouldContain("ö");
    }
}
