namespace HilmaAgent.Core.Retrieval;

/// <summary>
/// Turns text into vectors. Behind an interface because the provider is a swappable decision and
/// because the tests must run without a key or a network.
/// </summary>
/// <remarks>
/// Anthropic does not offer an embeddings endpoint, so this is necessarily a different vendor from
/// the model that writes the Phase 3 narrative.
/// </remarks>
public interface IEmbeddingProvider
{
    /// <summary>Model identifier, stored alongside each vector so a model change is detectable.</summary>
    string ModelId { get; }

    /// <summary>Vector length. Must match the Qdrant collection's configured size.</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeds a batch in one call. Order of the result matches the order of the input.
    /// </summary>
    Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
