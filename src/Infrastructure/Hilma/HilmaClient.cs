using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HilmaAgent.Core.Notices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HilmaAgent.Infrastructure.Hilma;

/// <summary>
/// Typed client over the Hilma AVP Read API. Rate limiting and retry live in the resilience
/// handler configured in <see cref="InfrastructureServiceCollectionExtensions"/>, not here.
/// </summary>
public class HilmaClient(HttpClient http, IOptions<HilmaOptions> options, ILogger<HilmaClient> logger) : IHilmaClient
{
    private static readonly string[] ResultArrayNames = ["notices", "items", "results", "value", "data", "content"];
    private static readonly string[] IdNames = ["noticeId", "notice_id", "id", "nationalId", "identifier"];
    private static readonly string[] PublicationDateNames =
        ["publicationDate", "publication_date", "datePublished", "published", "publishedAt", "dispatchDate"];

    private readonly HilmaOptions _options = options.Value;

    public async IAsyncEnumerable<HilmaNoticeRef> SearchAsync(
        DateTimeOffset publishedAfter,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        for (var page = 0; ; page++)
        {
            var url = $"{_options.SearchPath}?publishedAfter={Uri.EscapeDataString(publishedAfter.ToString("O"))}"
                      + $"&page={page}&size={_options.PageSize}";

            using var response = await http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var results = JsonPick.ArrayOf(document.RootElement, ResultArrayNames);
            if (results is null)
            {
                logger.LogWarning("Search response contained no recognisable result array; stopping at page {Page}.", page);
                yield break;
            }

            var count = 0;
            foreach (var item in results.Value.EnumerateArray())
            {
                count++;
                var id = item.ValueKind == JsonValueKind.String
                    ? item.GetString()
                    : JsonPick.String(item, IdNames);

                if (string.IsNullOrWhiteSpace(id))
                {
                    logger.LogWarning("Skipping search hit without an identifier: {Hit}", item.GetRawText());
                    continue;
                }

                yield return new HilmaNoticeRef(id, JsonPick.Date(item, PublicationDateNames));
            }

            logger.LogDebug("Search page {Page} returned {Count} hits.", page, count);
            if (count < _options.PageSize) yield break;
        }
    }

    public async Task<HilmaNoticeDocument?> GetNoticeAsync(string noticeId, CancellationToken ct = default)
    {
        var url = _options.NoticeDetailPath.Replace("{id}", Uri.EscapeDataString(noticeId));

        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            logger.LogWarning("Notice {NoticeId} reported as missing by the detail endpoint.", noticeId);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        return new HilmaNoticeDocument(noticeId, body, DateTimeOffset.UtcNow);
    }
}
