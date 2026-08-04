namespace HilmaAgent.Core.Notices;

/// <summary>Which part of a notice a chunk came from. Carried into the vector payload so citations can name it.</summary>
public static class ChunkSection
{
    /// <summary>Title, buyer, CPV codes, value, deadline — the facts a bidder screens on first.</summary>
    public const string Summary = "summary";

    /// <summary>The notice-level description of what is being procured.</summary>
    public const string Description = "description";

    /// <summary>One lot. Multi-lot notices are the reason chunking is section-level rather than per-notice.</summary>
    public const string Lot = "lot";
}

/// <summary>
/// An embeddable slice of a notice. One row per chunk; the vector itself lives in Qdrant and is
/// referenced by <see cref="VectorId"/>.
/// </summary>
public class NoticeChunk
{
    public Guid Id { get; set; }

    public required string NoticeId { get; set; }
    public Notice? Notice { get; set; }

    /// <summary>See <see cref="ChunkSection"/>.</summary>
    public required string Section { get; set; }

    /// <summary>Lot identifier for <see cref="ChunkSection.Lot"/> chunks, e.g. LOT-0000. Null otherwise.</summary>
    public string? LotId { get; set; }

    /// <summary>Position within the notice, stable across re-chunking so citations stay meaningful.</summary>
    public int ChunkIndex { get; set; }

    /// <summary>The text that was embedded.</summary>
    public required string Content { get; set; }

    /// <summary>Qdrant point id. Same value as <see cref="Id"/> — kept explicit so the coupling is visible.</summary>
    public Guid VectorId { get; set; }

    /// <summary>Model the vector was produced with. A model change invalidates the vector, not the chunk.</summary>
    public string? EmbeddingModel { get; set; }

    public DateTimeOffset? EmbeddedAt { get; set; }
}
