using System.ComponentModel.DataAnnotations;

namespace HilmaAgent.Infrastructure.Retrieval;

public class EmbeddingOptions
{
    public const string SectionName = "Embeddings";

    /// <summary>Azure OpenAI resource endpoint, e.g. https://my-resource.openai.azure.com/</summary>
    public string? Endpoint { get; set; }

    /// <summary>API key. user-secrets locally, environment variables elsewhere — never committed.</summary>
    public string? ApiKey { get; set; }

    /// <summary>Azure *deployment* name, which is not necessarily the model name.</summary>
    public string DeploymentName { get; set; } = "text-embedding-3-large";

    /// <summary>Recorded against every vector so a model swap is detectable rather than silent.</summary>
    public string ModelId { get; set; } = "text-embedding-3-large";

    /// <summary>
    /// Vector length. text-embedding-3-large is 3072 natively and supports shortening; whatever is set
    /// here must match the Qdrant collection, which is created with this value.
    /// </summary>
    [Range(64, 4096)]
    public int Dimensions { get; set; } = 3072;

    /// <summary>Texts per request. Azure rejects oversized batches; this is the throttle that keeps us under.</summary>
    [Range(1, 2048)]
    public int BatchSize { get; set; } = 64;

    /// <summary>
    /// Use a deterministic local stand-in instead of calling Azure. Lets the stack run end to end with
    /// no key — the vectors are meaningless, so retrieval quality is not testable in this mode.
    /// </summary>
    public bool UseFake { get; set; }
}
