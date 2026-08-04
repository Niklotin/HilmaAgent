using HilmaAgent.Core.Notices;
using HilmaAgent.Core.Retrieval;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace HilmaAgent.Infrastructure.Retrieval;

public class QdrantOptions
{
    public const string SectionName = "Qdrant";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 6334;
    public bool UseHttps { get; set; }
    public string? ApiKey { get; set; }
    public string CollectionName { get; set; } = "notice_chunks";
}

/// <summary>
/// Qdrant-backed vector store. Payload fields are duplicated from Postgres so that CPV, region, and
/// deadline filters run *inside* the search rather than after it — filtering a top-k list after the
/// fact silently shrinks it, which is the classic way to make good retrieval look bad.
/// </summary>
public class QdrantVectorStore(
    QdrantClient client,
    IOptions<QdrantOptions> options,
    ILogger<QdrantVectorStore> logger) : IVectorStore
{
    private readonly QdrantOptions _options = options.Value;

    public async Task EnsureCollectionAsync(int dimensions, CancellationToken ct = default)
    {
        var collections = await client.ListCollectionsAsync(cancellationToken: ct);
        if (collections.Contains(_options.CollectionName))
        {
            var info = await client.GetCollectionInfoAsync(_options.CollectionName, ct);
            var existing = (int)info.Config.Params.VectorsConfig.Params.Size;
            if (existing != dimensions)
                throw new InvalidOperationException(
                    $"Collection '{_options.CollectionName}' has {existing}-dimension vectors but the embedding " +
                    $"provider produces {dimensions}. Drop the collection or change the provider — the two must agree.");

            return;
        }

        await client.CreateCollectionAsync(
            _options.CollectionName,
            new VectorParams { Size = (ulong)dimensions, Distance = Distance.Cosine },
            cancellationToken: ct);

        // Payload indexes: without them Qdrant filters by scanning, which is fine at 100 notices and
        // not at 100,000.
        foreach (var field in new[] { "cpvCodes", "nutsCodes", "noticeId", "section" })
            await client.CreatePayloadIndexAsync(_options.CollectionName, field, PayloadSchemaType.Keyword, cancellationToken: ct);

        await client.CreatePayloadIndexAsync(_options.CollectionName, "deadline", PayloadSchemaType.Integer, cancellationToken: ct);

        logger.LogInformation("Created Qdrant collection {Collection} with {Dimensions} dimensions.",
            _options.CollectionName, dimensions);
    }

    public async Task UpsertAsync(IReadOnlyList<(NoticeChunk Chunk, float[] Vector)> points, CancellationToken ct = default)
    {
        if (points.Count == 0) return;

        var structs = points.Select(point =>
        {
            var notice = point.Chunk.Notice
                ?? throw new InvalidOperationException($"Chunk {point.Chunk.Id} needs its Notice loaded to build the payload.");

            var value = new PointStruct
            {
                Id = point.Chunk.VectorId,
                Vectors = point.Vector,
                Payload =
                {
                    ["noticeId"] = point.Chunk.NoticeId,
                    ["section"] = point.Chunk.Section,
                    ["content"] = point.Chunk.Content,
                },
            };

            if (point.Chunk.LotId is { } lotId) value.Payload["lotId"] = lotId;
            if (notice.Title is { } title) value.Payload["title"] = title;
            if (notice.BuyerName is { } buyer) value.Payload["buyerName"] = buyer;

            value.Payload["cpvCodes"] = notice.CpvCodes.ToArray();
            value.Payload["nutsCodes"] = notice.Region.ToArray();

            // Stored as epoch seconds: Qdrant range filters are numeric, and "still open" is a range
            // query against now.
            if (notice.SubmissionDeadline is { } deadline)
                value.Payload["deadline"] = deadline.ToUnixTimeSeconds();

            return value;
        }).ToList();

        await client.UpsertAsync(_options.CollectionName, structs, cancellationToken: ct);
        logger.LogDebug("Upserted {Count} vectors.", structs.Count);
    }

    public async Task<IReadOnlyList<VectorHit>> SearchAsync(
        float[] query,
        int limit,
        VectorSearchFilter? filter = null,
        CancellationToken ct = default)
    {
        var results = await client.SearchAsync(
            _options.CollectionName,
            query,
            filter: BuildFilter(filter),
            limit: (ulong)limit,
            cancellationToken: ct);

        return results.Select(point => new VectorHit(
            Guid.Parse(point.Id.Uuid),
            point.Payload["noticeId"].StringValue,
            point.Payload["section"].StringValue,
            point.Payload.TryGetValue("lotId", out var lotId) ? lotId.StringValue : null,
            point.Score)).ToList();
    }

    /// <summary>
    /// CPV and NUTS codes are hierarchical strings, so prefix matching is what "IT services" or
    /// "Uusimaa" actually means. Qdrant has no prefix operator on keyword fields, so each prefix
    /// becomes a text match and the set is OR-ed.
    /// </summary>
    private static Filter? BuildFilter(VectorSearchFilter? filter)
    {
        if (filter is null) return null;

        var conditions = new List<Condition>();

        if (filter.CpvPrefixes is { Count: > 0 } cpv)
            conditions.Add(AnyPrefix("cpvCodes", cpv));

        if (filter.NutsPrefixes is { Count: > 0 } nuts)
            conditions.Add(AnyPrefix("nutsCodes", nuts));

        if (filter.OpenOnly)
        {
            conditions.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = "deadline",
                    Range = new Qdrant.Client.Grpc.Range { Gte = DateTimeOffset.UtcNow.ToUnixTimeSeconds() },
                },
            });
        }

        if (conditions.Count == 0) return null;

        var result = new Filter();
        result.Must.AddRange(conditions);
        return result;
    }

    private static Condition AnyPrefix(string field, IReadOnlyList<string> prefixes)
    {
        var any = new Filter();
        foreach (var prefix in prefixes)
            any.Should.Add(new Condition { Field = new FieldCondition { Key = field, Match = new Match { Text = prefix } } });

        return new Condition { Filter = any };
    }

    public async Task DeleteByNoticeAsync(string noticeId, CancellationToken ct = default)
    {
        var filter = new Filter();
        filter.Must.Add(new Condition { Field = new FieldCondition { Key = "noticeId", Match = new Match { Keyword = noticeId } } });
        await client.DeleteAsync(_options.CollectionName, filter, cancellationToken: ct);
    }

    public async Task<long> CountAsync(CancellationToken ct = default) =>
        (long)await client.CountAsync(_options.CollectionName, cancellationToken: ct);
}
