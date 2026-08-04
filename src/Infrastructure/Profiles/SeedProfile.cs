using HilmaAgent.Core.Profiles;

namespace HilmaAgent.Infrastructure.Profiles;

/// <summary>
/// The demo company profile.
/// </summary>
/// <remarks>
/// <b>Sammalkoski Digital Oy is fictional.</b> It was invented for this project rather than modelled
/// on a real supplier — screening real notices against a real company's stated capabilities, and
/// publishing the resulting GO/NO-GO calls, would be putting words in someone else's mouth. The
/// notices are public records; the company reading them is made up.
/// <para>It is drawn as a mid-sized Helsinki software consultancy because that is the profile most
/// likely to find the IT-services notices in the corpus interesting, which makes the demo show
/// something rather than reject everything.</para>
/// </remarks>
public static class SeedProfile
{
    public static readonly Guid Id = Guid.Parse("a1e5c3d2-0000-4000-8000-000000000001");

    public static CompanyProfile Create() => new()
    {
        Id = Id,
        Name = "Sammalkoski Digital Oy",
        Description =
            "Fictional company, invented for this demo. A Helsinki-based software consultancy of about " +
            "35 people, specialising in custom business systems and integrations for Finnish public-sector " +
            "buyers: municipalities, wellbeing services counties, and state agencies. Delivers on .NET and " +
            "Azure with React front ends, and takes on both greenfield builds and the modernisation of " +
            "ageing in-house systems. Works as prime contractor on mid-sized projects and as a subcontractor " +
            "on larger programmes.",
        Technologies =
        [
            ".NET", "C#", "ASP.NET Core", "Azure", "React", "TypeScript", "PostgreSQL", "SQL Server",
            "Docker", "Kubernetes", "REST-rajapinnat", "integraatiot", "CI/CD", "Power BI",
        ],
        ReferenceProjects =
        [
            "Asianhallintajärjestelmän uudistus keskisuurelle kaupungille: vanhan järjestelmän korvaaminen " +
            "ASP.NET Core -pohjaisella ratkaisulla, käyttäjiä noin 600, projektin kesto 18 kuukautta.",

            "Hyvinvointialueen potilastietojärjestelmien välinen integraatioalusta: HL7- ja REST-rajapinnat, " +
            "sanomanvälitys ja valvonta Azure-ympäristössä.",

            "Valtion viraston avoimen datan rajapinta ja siihen liittyvä hallintakäyttöliittymä: " +
            "julkinen REST-API, käyttöoikeuksien hallinta ja raportointi.",

            "Kuntayhtymän talousraportoinnin tietoalusta: tietovarasto, ETL-prosessit ja Power BI -raportit " +
            "korvaamassa käsin koottuja Excel-raportteja.",

            "Ylläpito- ja jatkokehityssopimus kaupungin lupa-asiointipalvelusta, kolmas vuosi käynnissä.",
        ],
        // Deliberately a mix of division-level and specific codes: the scorer treats a division as
        // containing everything under it, so the broad entries widen the net while the specific ones
        // score higher when they hit exactly.
        PreferredCpvCodes =
        [
            "72000000",  // IT services: consulting, software development, Internet and support
            "72200000",  // Software programming and consultancy services
            "72300000",  // Data services
            "72500000",  // Computer-related services
            "72600000",  // Computer support and consultancy services
            "48000000",  // Software package and information systems
        ],
        // Helsinki-Uusimaa as the home market, Southern Finland as the reachable one. Not nationwide:
        // a 35-person consultancy that claims to serve everywhere is not being honest with itself.
        Regions = ["FI1B", "FI1C"],
        MinContractValue = 50_000,
        MaxContractValue = 3_000_000,
    };
}
