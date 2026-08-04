using System.Net;
using System.Threading.RateLimiting;
using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using HilmaAgent.Infrastructure.Ingestion;
using HilmaAgent.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace HilmaAgent.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HilmaOptions>()
            .Bind(configuration.GetSection(HilmaOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddDbContext<HilmaDbContext>(options =>
            options.UseNpgsql(configuration.GetConnectionString("Postgres")));

        services.AddSingleton<INoticeParser, TolerantNoticeParser>();
        services.AddScoped<NoticeIngestionService>();

        services.AddHttpClient<IHilmaClient, HilmaClient>((provider, http) =>
        {
            var hilma = provider.GetRequiredService<IOptions<HilmaOptions>>().Value;

            http.BaseAddress = new Uri(hilma.BaseUrl.TrimEnd('/') + "/");
            http.Timeout = hilma.RequestTimeout;
            if (!string.IsNullOrWhiteSpace(hilma.SubscriptionKey))
                http.DefaultRequestHeaders.Add(hilma.SubscriptionKeyHeader, hilma.SubscriptionKey);
        })
        .AddResilienceHandler("hilma", (builder, context) =>
        {
            var hilma = context.ServiceProvider.GetRequiredService<IOptions<HilmaOptions>>().Value;

            // Rate limiter first: retries must queue behind it too, otherwise a burst of retries
            // is exactly what tips us over the API's published limit.
            builder.AddRateLimiter(new SlidingWindowRateLimiter(new SlidingWindowRateLimiterOptions
            {
                PermitLimit = hilma.RequestsPerWindow,
                Window = hilma.RateLimitWindow,
                SegmentsPerWindow = 6,
                QueueLimit = int.MaxValue,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            }));

            builder.AddRetry(new HttpRetryStrategyOptions
            {
                MaxRetryAttempts = hilma.MaxRetryAttempts,
                Delay = hilma.RetryBaseDelay,
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                ShouldHandle = args => ValueTask.FromResult(args.Outcome switch
                {
                    { Exception: HttpRequestException } => true,
                    { Result: { } response } => IsTransient(response.StatusCode),
                    _ => false,
                }),
            });
        });

        return services;
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests
        || status == HttpStatusCode.RequestTimeout
        || (int)status >= 500;
}
