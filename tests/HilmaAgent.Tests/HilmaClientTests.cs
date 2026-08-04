using System.Net;
using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace HilmaAgent.Tests;

/// <summary>
/// Exercises the real client + resilience pipeline against a stubbed Hilma API.
/// No live API key, no network.
/// </summary>
public class HilmaClientTests : IDisposable
{
    private readonly WireMockServer _server = WireMockServer.Start();

    private IHilmaClient CreateClient(int pageSize = 2)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Postgres"] = "Host=localhost;Database=unused;Username=u;Password=p",
            ["Hilma:BaseUrl"] = _server.Url!,
            ["Hilma:SubscriptionKey"] = "test-key",
            ["Hilma:PageSize"] = pageSize.ToString(),
            // Keep retries fast; the pipeline's behaviour is what matters, not its wall time.
            ["Hilma:RetryBaseDelay"] = "00:00:00.010",
            ["Hilma:RequestsPerWindow"] = "1000",
        }).Build();

        var services = new ServiceCollection()
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();

        return services.GetRequiredService<IHilmaClient>();
    }

    [Fact]
    public async Task Search_pages_until_a_short_page_and_yields_identifiers()
    {
        _server.Given(Request.Create().WithPath("/notices").WithParam("page", "0").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new
            {
                notices = new[]
                {
                    new { noticeId = "n-1", publicationDate = "2026-07-01T00:00:00Z" },
                    new { noticeId = "n-2", publicationDate = "2026-07-02T00:00:00Z" },
                },
            }));

        _server.Given(Request.Create().WithPath("/notices").WithParam("page", "1").UsingGet())
            .RespondWith(Response.Create().WithBodyAsJson(new
            {
                notices = new[] { new { noticeId = "n-3", publicationDate = "2026-07-03T00:00:00Z" } },
            }));

        var refs = new List<HilmaNoticeRef>();
        await foreach (var reference in CreateClient().SearchAsync(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero)))
            refs.Add(reference);

        refs.Select(r => r.NoticeId).ShouldBe(["n-1", "n-2", "n-3"]);
        refs[0].PublicationDate.ShouldBe(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Detail_request_sends_the_subscription_key_and_returns_raw_json()
    {
        _server.Given(Request.Create().WithPath("/api/avp/notices/n-1")
                .WithHeader("Ocp-Apim-Subscription-Key", "test-key").UsingGet())
            .RespondWith(Response.Create().WithBody(Fixture.Read("notice-contract-example.json")));

        var document = await CreateClient().GetNoticeAsync("n-1");

        document.ShouldNotBeNull();
        document.NoticeId.ShouldBe("n-1");
        document.RawJson.ShouldContain("Cars for the London office");
    }

    [Fact]
    public async Task Retries_a_429_then_succeeds()
    {
        _server.Given(Request.Create().WithPath("/api/avp/notices/n-throttled").UsingGet())
            .InScenario("throttle").WillSetStateTo("second-attempt")
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.TooManyRequests));

        _server.Given(Request.Create().WithPath("/api/avp/notices/n-throttled").UsingGet())
            .InScenario("throttle").WhenStateIs("second-attempt")
            .RespondWith(Response.Create().WithBody("""{"noticeId":"n-throttled"}"""));

        var document = await CreateClient().GetNoticeAsync("n-throttled");

        document.ShouldNotBeNull();
        _server.LogEntries.Count(entry => entry.RequestMessage?.Path == "/api/avp/notices/n-throttled").ShouldBe(2);
    }

    [Fact]
    public async Task Missing_notice_returns_null_rather_than_throwing()
    {
        _server.Given(Request.Create().WithPath("/api/avp/notices/gone").UsingGet())
            .RespondWith(Response.Create().WithStatusCode(HttpStatusCode.NotFound));

        (await CreateClient().GetNoticeAsync("gone")).ShouldBeNull();
    }

    public void Dispose() => _server.Dispose();
}

