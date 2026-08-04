using System.ClientModel;
using Azure.AI.OpenAI;
using HilmaAgent.Core.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace HilmaAgent.Infrastructure.Retrieval;

/// <summary>
/// Embeddings via an Azure OpenAI deployment. Chosen for Finnish: the notices, and therefore the
/// queries, are Finnish, which rules out English-only models.
/// </summary>
public class AzureOpenAIEmbeddingProvider(
    IOptions<EmbeddingOptions> options,
    ILogger<AzureOpenAIEmbeddingProvider> logger) : IEmbeddingProvider
{
    private readonly EmbeddingOptions _options = options.Value;
    private readonly ILogger<AzureOpenAIEmbeddingProvider> _logger = logger;

    /// <summary>
    /// Built on first use rather than in the constructor: missing credentials should fail the
    /// indexing pass that needs them, not the whole host at startup.
    /// </summary>
    private readonly Lazy<EmbeddingClient> _client = new(() =>
    {
        var value = options.Value;

        if (string.IsNullOrWhiteSpace(value.Endpoint) || string.IsNullOrWhiteSpace(value.ApiKey))
            throw new InvalidOperationException(
                "Embeddings:Endpoint and Embeddings:ApiKey are required to embed. " +
                "Set Embeddings:UseFake=true to run the stack without them.");

        var azure = new AzureOpenAIClient(new Uri(value.Endpoint), new ApiKeyCredential(value.ApiKey));
        return azure.GetEmbeddingClient(value.DeploymentName);
    });

    public string ModelId => _options.ModelId;

    public int Dimensions => _options.Dimensions;

    public async Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default)
    {
        if (texts.Count == 0) return [];

        var vectors = new List<float[]>(texts.Count);

        // Batched because the per-call limit is the binding constraint, and because one request per
        // chunk would make ingesting a day of notices needlessly slow.
        foreach (var batch in texts.Chunk(_options.BatchSize))
        {
            ct.ThrowIfCancellationRequested();

            var response = await _client.Value.GenerateEmbeddingsAsync(
                batch,
                new EmbeddingGenerationOptions { Dimensions = _options.Dimensions },
                ct);

            // Order is guaranteed to match the input; asserting it here would be cheap insurance if
            // that ever stopped holding, but the client already indexes by position.
            vectors.AddRange(response.Value.Select(embedding => embedding.ToFloats().ToArray()));
        }

        _logger.LogDebug("Embedded {Count} texts with {Model}.", texts.Count, ModelId);
        return vectors;
    }
}
