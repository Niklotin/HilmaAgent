using System.Net;
using System.Threading.RateLimiting;
using HilmaAgent.Core.Notices;
using HilmaAgent.Infrastructure.Hilma;
using HilmaAgent.Core.Retrieval;
using HilmaAgent.Infrastructure.Ingestion;
using HilmaAgent.Infrastructure.Persistence;
using HilmaAgent.Infrastructure.Retrieval;
using Qdrant.Client;
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

        services.AddSingleton<INoticeParser, NoticeContractParser>();
        services.AddSingleton<SearchIndexNoticeParser>();
        services.AddScoped<NoticeIngestionService>();

        AddRetrieval(services, configuration);

        services.AddHttpClient<IHilmaClient, HilmaClient>((provider, http) =>
        {
            var hilma = provider.GetRequiredService<IOptions<HilmaOptions>>().Value;

            // No BaseAddress: search and read live on different base URLs, so the client builds
            // absolute URIs from options instead.
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

    /// <summary>Phase 2: chunking, embeddings, and the Qdrant-backed vector store.</summary>
    private static void AddRetrieval(IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<EmbeddingOptions>()
            .Bind(configuration.GetSection(EmbeddingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<QdrantOptions>()
            .Bind(configuration.GetSection(QdrantOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<NoticeChunker>();

        // The stand-in provider keeps the stack runnable without an Azure key; it is opt-in so a
        // missing key fails loudly at startup rather than silently producing meaningless vectors.
        var embeddings = configuration.GetSection(EmbeddingOptions.SectionName).Get<EmbeddingOptions>() ?? new EmbeddingOptions();
        if (embeddings.UseFake)
            services.AddSingleton<IEmbeddingProvider, DeterministicEmbeddingProvider>();
        else
            services.AddSingleton<IEmbeddingProvider, AzureOpenAIEmbeddingProvider>();

        services.AddSingleton(provider =>
        {
            var qdrant = provider.GetRequiredService<IOptions<QdrantOptions>>().Value;
            return new QdrantClient(qdrant.Host, qdrant.Port, qdrant.UseHttps, qdrant.ApiKey);
        });

        services.AddSingleton<IVectorStore, QdrantVectorStore>();
        services.AddScoped<NoticeIndexingService>();
        services.AddScoped<NoticeSearchService>();
        services.AddScoped<RetrievalEvaluator>();
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests
        || status == HttpStatusCode.RequestTimeout
        || (int)status >= 500;
}
