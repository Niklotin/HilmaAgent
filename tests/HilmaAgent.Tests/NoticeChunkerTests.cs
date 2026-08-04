using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using HilmaAgent.Infrastructure.Retrieval;
using Shouldly;

namespace HilmaAgent.Tests;

public class NoticeChunkerTests
{
    private readonly NoticeChunker _chunker = new();

    private static Notice EFormsNotice(string document, string searchId = "EF-1") =>
        new SearchIndexNoticeParser().Parse(new HilmaNoticeRef(
            searchId, 1, true, new DateTimeOffset(2026, 7, 5, 0, 0, 0, TimeSpan.Zero), document));

    [Fact]
    public void Summary_chunk_carries_the_facts_a_bidder_screens_on()
    {
        var notice = EFormsNotice("""
        {"id":"EF-1","noticeId":1,"isEForms":true,"titleFi":"Ohjelmistokehityksen asiantuntijapalvelut",
         "organisationNameFi":"Helsingin kaupunki","mainType":"ContractNotices",
         "cpvCodes":"72000000 72200000","nutsCodes":"FI1B1","estimatedValue":450000,"currency":"EUR",
         "deadline":"2026-09-01T12:00:00Z"}
        """);

        var summary = _chunker.Chunk(notice).Single(c => c.Section == ChunkSection.Summary);

        // The chunk has to stand alone as a citation — no second lookup to make sense of it.
        summary.Content.ShouldContain("Ohjelmistokehityksen asiantuntijapalvelut");
        summary.Content.ShouldContain("Helsingin kaupunki");
        summary.Content.ShouldContain("72000000, 72200000");
        summary.Content.ShouldContain("FI1B1");
        summary.Content.ShouldContain("450,000 EUR");
        summary.Content.ShouldContain("2026-09-01");
    }

    [Fact]
    public void Each_lot_becomes_its_own_chunk()
    {
        var notice = EFormsNotice("""
        {"id":"EF-2","noticeId":2,"isEForms":true,"titleFi":"Monialainen puitejärjestely",
         "cpvCodes":"45000000 72000000",
         "lots":[
           {"id":"LOT-0000","titleFi":"Tienpäällystystyöt","cpvCodes":"45233220","nutsCodes":"FI1C1",
            "descriptionFi":"Päällystystyöt Uudenmaan alueella."},
           {"id":"LOT-0001","titleFi":"IT-konsultointi","cpvCodes":"72000000","nutsCodes":"FI1B1",
            "descriptionFi":"Ohjelmistokehityksen asiantuntijatyö."}
         ]}
        """);

        var lots = _chunker.Chunk(notice).Where(c => c.Section == ChunkSection.Lot).ToList();

        // The whole point of section-level chunking: road resurfacing and IT consultancy in one
        // notice must not be averaged into a single vector that matches neither.
        lots.Count.ShouldBe(2);
        lots.Select(c => c.LotId).ShouldBe(["LOT-0000", "LOT-0001"]);
        lots[0].Content.ShouldContain("Tienpäällystystyöt");
        lots[0].Content.ShouldNotContain("IT-konsultointi");
        lots[1].Content.ShouldContain("Ohjelmistokehityksen");
    }

    [Fact]
    public void Chunk_indexes_are_contiguous_and_ids_match_vector_ids()
    {
        var notice = EFormsNotice("""
        {"id":"EF-3","noticeId":3,"isEForms":true,"titleFi":"Testi","descriptionFi":"Kuvaus tästä hankinnasta.",
         "lots":[{"id":"LOT-0000","titleFi":"Osa","descriptionFi":"Lisätietoa."}]}
        """, searchId: "EF-3");

        var chunks = _chunker.Chunk(notice);

        chunks.Select(c => c.ChunkIndex).ShouldBe(Enumerable.Range(0, chunks.Count));
        chunks.ShouldAllBe(c => c.Id == c.VectorId);
        chunks.ShouldAllBe(c => c.NoticeId == "EF-3");
    }

    [Fact]
    public void Long_descriptions_split_on_paragraph_boundaries_not_mid_word()
    {
        var paragraph = string.Join(" ", Enumerable.Repeat("hankinnan kohteena on laaja kokonaisuus.", 60));
        var document = $$"""
        {"id":"EF-4","noticeId":4,"isEForms":true,"titleFi":"Pitkä",
         "descriptionFi":"{{paragraph}}\n\n{{paragraph}}\n\n{{paragraph}}"}
        """;

        var descriptions = _chunker.Chunk(EFormsNotice(document))
            .Where(c => c.Section == ChunkSection.Description).ToList();

        descriptions.Count.ShouldBeGreaterThan(1);
        descriptions.ShouldAllBe(c => c.Content.Length <= NoticeChunker.MaxChunkCharacters + 200);
        // A chunk starting mid-word embeds badly and reads worse as a citation.
        descriptions.ShouldAllBe(c => c.Content.EndsWith('.'));
    }

    [Fact]
    public void Legacy_notices_chunk_from_their_own_contract_shape()
    {
        var notice = new NoticeContractParser().Parse(new HilmaNoticeDocument("160524", """
        {"id":160524,"type":9905,
         "project":{"title":"Logistiikan koulutusrakennuksen suunnittelu"},
         "procurementObject":{"shortDescription":["Suorahankinta suunnittelupalveluista."],
                              "mainCpvCode":{"code":"71240000"}},
         "objectDescriptions":[{"lotNumber":"1","title":"Suunnittelu","nutsCodes":["FI1C4"],
                                "descrProcurement":["Arkkitehti- ja rakennesuunnittelu."],
                                "mainCpvCode":{"code":"71240000"}}]}
        """, DateTimeOffset.UnixEpoch));
        notice.Id = "OLD-160524";

        var chunks = _chunker.Chunk(notice);

        chunks.ShouldContain(c => c.Section == ChunkSection.Description && c.Content.Contains("Suorahankinta"));
        var lot = chunks.Single(c => c.Section == ChunkSection.Lot);
        lot.Content.ShouldContain("Arkkitehti- ja rakennesuunnittelu");
        lot.Content.ShouldContain("71240000");
    }

    [Fact]
    public void A_notice_with_no_text_still_yields_its_summary()
    {
        var notice = EFormsNotice("""{"id":"EF-5","noticeId":5,"isEForms":true,"titleFi":"Pelkkä otsikko"}""");

        var chunks = _chunker.Chunk(notice);

        chunks.Count.ShouldBe(1);
        chunks[0].Section.ShouldBe(ChunkSection.Summary);
    }
}
