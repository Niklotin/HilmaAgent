using System.ComponentModel.DataAnnotations;

namespace HilmaAgent.Infrastructure.Hilma;

public class HilmaOptions
{
    public const string SectionName = "Hilma";

    /// <summary>Base address of the Hilma AVP Read API. Verify against the developer portal.</summary>
    [Required]
    public string BaseUrl { get; set; } = "https://api.hankintailmoitukset.fi/";

    /// <summary>
    /// APIM subscription key. Kept in user-secrets locally, environment variables elsewhere.
    /// Never committed.
    /// </summary>
    public string? SubscriptionKey { get; set; }

    /// <summary>Header the APIM gateway expects the subscription key in.</summary>
    public string SubscriptionKeyHeader { get; set; } = "Ocp-Apim-Subscription-Key";

    /// <summary>Relative path of the search endpoint. TODO: confirm against live API docs.</summary>
    public string SearchPath { get; set; } = "notices";

    /// <summary>
    /// Relative path template of the detail endpoint; <c>{id}</c> is substituted.
    /// TODO: confirm against live API docs.
    /// </summary>
    public string NoticeDetailPath { get; set; } = "notices/{id}";

    /// <summary>Page size requested from the search endpoint.</summary>
    [Range(1, 500)]
    public int PageSize { get; set; } = 100;

    /// <summary>Requests permitted per <see cref="RateLimitWindow"/>. Tune once the published limits are known.</summary>
    [Range(1, 1000)]
    public int RequestsPerWindow { get; set; } = 30;

    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>How long a request may queue behind the rate limiter before failing.</summary>
    public TimeSpan RateLimitQueueTimeout { get; set; } = TimeSpan.FromMinutes(2);

    [Range(1, 10)]
    public int MaxRetryAttempts { get; set; } = 4;

    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(30);
}
