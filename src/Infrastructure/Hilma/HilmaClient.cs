using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using HilmaAgent.Core.Notices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HilmaAgent.Infrastructure.Hilma;

/// <summary>
/// Typed client over the Hilma AVP APIs. Search and read are separate services on different base
/// URLs behind one subscription key, so URIs are built absolute rather than from a BaseAddress.
/// Rate limiting and retry live in the resilience handler configured in
/// <see cref="InfrastructureServiceCollectionExtensions"/>, not here.
/// </summary>
public class HilmaClient(HttpClient http, IOptions<HilmaOptions> options, ILogger<HilmaClient> logger) : IHilmaClient
{
    private readonly HilmaOptions _options = options.Value;

    /// <summary>
    /// The search endpoint is an Azure Cognitive Search passthrough: POST with a query document,
    /// <c>top</c>/<c>skip</c> paging, and an OData <c>filter</c>.
    /// </summary>
    public async IAsyncEnumerable<HilmaNoticeRef> SearchAsync(
        DateTimeOffset publishedAfter,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var url = $"{_options.SearchBaseUrl.TrimEnd('/')}/{_options.SearchPath.TrimStart('/')}";

        for (var skip = 0; ; skip += _options.PageSize)
        {
            var query = new
            {
                search = "*",
                // Plans carry noticeId 0 and have no notice behind them; excluding them here keeps
                // the ingestion loop from fetching identifiers that can never resolve.
                filter = $"datePublished gt {publishedAfter.UtcDateTime:yyyy-MM-ddTHH:mm:ss.fffZ} and isPlan eq false",
                orderby = "datePublished asc",
                top = _options.PageSize.ToString(),
                skip = skip.ToString(),
                count = "true",
            };

            using var response = await http.PostAsJsonAsync(url, query, ct);
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!document.RootElement.TryGetProperty("value", out var hits) || hits.ValueKind != JsonValueKind.Array)
            {
                logger.LogWarning("Search response had no 'value' array; stopping at skip {Skip}.", skip);
                yield break;
            }

            var count = 0;
            foreach (var hit in hits.EnumerateArray())
            {
                count++;
                var searchId = hit.TryGetProperty("id", out var id) ? id.GetString() : null;
                if (string.IsNullOrWhiteSpace(searchId))
                {
                    logger.LogWarning("Skipping search hit without an index key.");
                    continue;
                }

                yield return new HilmaNoticeRef(
                    searchId,
                    hit.TryGetProperty("noticeId", out var noticeId) && noticeId.TryGetInt32(out var parsed) ? parsed : 0,
                    hit.TryGetProperty("isEForms", out var eForms) && eForms.ValueKind == JsonValueKind.True,
                    hit.TryGetProperty("datePublished", out var published)
                    && DateTimeOffset.TryParse(published.GetString(), out var date) ? date.ToUniversalTime() : null,
                    hit.GetRawText());
            }

            logger.LogDebug("Search page at skip {Skip} returned {Count} hits.", skip, count);
            if (count < _options.PageSize) yield break;
        }
    }

    public async Task<HilmaNoticeDocument?> GetNoticeAsync(string noticeId, CancellationToken ct = default)
    {
        var path = _options.NoticeDetailPath.Replace("{id}", Uri.EscapeDataString(noticeId));
        var url = $"{_options.ReadBaseUrl.TrimEnd('/')}/{path.TrimStart('/')}";

        using var response = await http.GetAsync(url, ct);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            // Expected for eForms notices: the legacy Read API does not serve them.
            logger.LogDebug("Notice {NoticeId} not available from the Read API (404).", noticeId);
            return null;
        }

        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadAsStringAsync(ct);
        return new HilmaNoticeDocument(noticeId, body, DateTimeOffset.UtcNow);
    }
}
