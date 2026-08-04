using HilmaAgent.Infrastructure.Ingestion;
using HilmaAgent.Infrastructure.Retrieval;
using Microsoft.Extensions.Options;

namespace HilmaAgent.Worker;

public class IngestionWorkerOptions
{
    public const string SectionName = "Ingestion";

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Delay before the first pass, so the database has finished migrating.</summary>
    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>Cap on notices fetched per pass — the detail endpoint is one call per notice.</summary>
    public int MaxNoticesPerRun { get; set; } = 100;

    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Chunk and embed newly ingested notices in the same pass. Turn off to ingest without an
    /// embedding provider configured.
    /// </summary>
    public bool IndexAfterIngestion { get; set; } = true;

    /// <summary>Cap on notices indexed per pass. Embedding is billed per token, so this is a cost lever.</summary>
    public int MaxNoticesIndexedPerRun { get; set; } = 200;
}

public class IngestionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<IngestionWorkerOptions> options,
    ILogger<IngestionWorker> logger) : BackgroundService
{
    private readonly IngestionWorkerOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            logger.LogInformation("Ingestion worker disabled by configuration.");
            return;
        }

        try
        {
            await Task.Delay(_options.InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(_options.Interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<NoticeIngestionService>();
                await ingestion.RunAsync(_options.MaxNoticesPerRun, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Keep the schedule alive: a failed pass resumes from the same checkpoint next tick.
                logger.LogError(ex, "Ingestion pass failed; retrying at the next interval.");
            }

            if (_options.IndexAfterIngestion)
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var indexing = scope.ServiceProvider.GetRequiredService<NoticeIndexingService>();
                    await indexing.RunAsync(_options.MaxNoticesIndexedPerRun, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    // Indexing failing must not stop ingestion: notices already in Postgres are the
                    // durable asset, and un-indexed ones are picked up on the next pass.
                    logger.LogError(ex, "Indexing pass failed; notices remain ingested and will be indexed later.");
                }
            }

            try
            {
                if (!await timer.WaitForNextTickAsync(stoppingToken)) return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
