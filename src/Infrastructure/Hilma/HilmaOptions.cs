using System.ComponentModel.DataAnnotations;

namespace HilmaAgent.Infrastructure.Hilma;

public class HilmaOptions
{
    public const string SectionName = "Hilma";

    /// <summary>
    /// Base address of the Search API. Note this differs from <see cref="ReadBaseUrl"/> — search and
    /// read are separate APIs behind the same subscription key.
    /// </summary>
    [Required]
    public string SearchBaseUrl { get; set; } = "https://api.hankintailmoitukset.fi/avp";

    /// <summary>Base address of the legacy Read API (notice detail as JSON).</summary>
    [Required]
    public string ReadBaseUrl { get; set; } = "https://api.hankintailmoitukset.fi/avp-notice";

    /// <summary>Base address of the eForms Read API (notice content as base64 UBL XML).</summary>
    [Required]
    public string EFormsBaseUrl { get; set; } = "https://api.hankintailmoitukset.fi/avp-eforms";

    /// <summary>Batch detail path for eForms notices; ids are appended as repeated <c>id</c> query parameters.</summary>
    public string EFormsBatchPath { get; set; } = "external-read/v1/notices";

    /// <summary>Ids per batch request. The API rejects more than 50.</summary>
    [Range(1, 50)]
    public int EFormsBatchSize { get; set; } = 50;

    /// <summary>
    /// APIM subscription key. Kept in user-secrets locally, environment variables elsewhere.
    /// Never committed.
    /// </summary>
    public string? SubscriptionKey { get; set; }

    /// <summary>Header the APIM gateway expects the subscription key in.</summary>
    public string SubscriptionKeyHeader { get; set; } = "Ocp-Apim-Subscription-Key";

    /// <summary>
    /// Relative path of the search endpoint. The eForms index is the current one and covers both
    /// eForms and non-eForms notices; <c>/notices/docs/search</c> is deprecated and has indexed
    /// nothing since 2023-09-01.
    /// </summary>
    public string SearchPath { get; set; } = "eformnotices/docs/search";

    /// <summary>
    /// Relative path template of the detail endpoint; <c>{id}</c> is substituted.
    /// Confirmed against the Read API OpenAPI document.
    /// </summary>
    public string NoticeDetailPath { get; set; } = "api/avp/notices/{id}";

    /// <summary>Page size requested from the search endpoint. Azure Cognitive Search caps `top` at 1000.</summary>
    [Range(1, 1000)]
    public int PageSize { get; set; } = 200;

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
