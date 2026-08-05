using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using Shouldly;

namespace HilmaAgent.Tests;

/// <summary>
/// The eForms path, whose structured fields come from the search index document rather than the XML.
/// </summary>
public class SearchIndexNoticeParserTests
{
    private static Notice Parse(string document) => new SearchIndexNoticeParser()
        .Parse(new HilmaNoticeRef("EF-1", 1, true, DateTimeOffset.UnixEpoch, document));

    /// <summary>
    /// Where a bid is actually submitted, which is what turns an approved assessment into an action.
    /// </summary>
    [Fact]
    public void Reads_the_link_to_the_tender_documents()
    {
        var notice = Parse("""
        {"id":"EF-1","procurementDocumentsUrl":"https://tarjouspalvelu.fi/veikkaus?id=613395"}
        """);

        notice.ProcurementDocumentsUrl.ShouldBe("https://tarjouspalvelu.fi/veikkaus?id=613395");
    }

    [Fact]
    public void Leaves_the_link_null_when_the_notice_carries_none()
    {
        // Only about half of eForms notices state one, and legacy notices never do. A missing link
        // has to stay missing — the shortlist says so rather than linking somewhere invented.
        Parse("""{"id":"EF-1"}""").ProcurementDocumentsUrl.ShouldBeNull();
    }
}
