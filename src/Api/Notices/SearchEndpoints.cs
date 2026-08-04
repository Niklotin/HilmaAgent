using HilmaAgent.Core.Retrieval;
using HilmaAgent.Infrastructure.Retrieval;

namespace HilmaAgent.Api.Notices;

/// <param name="Query">Free-text query, in Finnish for best results — the corpus is Finnish.</param>
/// <param name="CpvPrefixes">Restrict to CPV code prefixes, e.g. ["72"] for IT services.</param>
/// <param name="NutsPrefixes">Restrict to NUTS region prefixes, e.g. ["FI1B"] for Helsinki-Uusimaa.</param>
/// <param name="OpenOnly">Exclude notices whose submission deadline has passed.</param>
public record SearchRequest(
    string Query,
    int Limit = 10,
    List<string>? CpvPrefixes = null,
    List<string>? NutsPrefixes = null,
    bool OpenOnly = false);

public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/search").WithTags("Search");

        group.MapPost("/", async (SearchRequest request, NoticeSearchService search, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.Query))
                return Results.BadRequest(new { error = "Query is required." });

            var filter = new VectorSearchFilter(request.CpvPrefixes, request.NutsPrefixes, request.OpenOnly);
            var results = await search.SearchAsync(request.Query, Math.Clamp(request.Limit, 1, 50), filter, ct);

            return Results.Ok(new { query = request.Query, count = results.Count, results });
        })
        .WithName("SearchNotices")
        .WithSummary("Semantic search over the notice index. Filters are applied inside the vector search, not after it.");

        group.MapPost("/index", async (NoticeIndexingService indexing, CancellationToken ct, int max = 200) =>
            Results.Ok(await indexing.RunAsync(Math.Clamp(max, 1, 5000), ct)))
        .WithName("IndexNotices")
        .WithSummary("Chunks and embeds notices that have no vectors for the configured model.");

        group.MapPost("/evaluate", async (
            RetrievalEvaluator evaluator,
            IWebHostEnvironment environment,
            CancellationToken ct) =>
        {
            var path = Path.Combine(environment.ContentRootPath, "eval-queries.json");
            if (!File.Exists(path)) return Results.NotFound(new { error = $"Query set not found at {path}." });

            try
            {
                return Results.Ok(await evaluator.EvaluateAsync(RetrievalEvaluator.LoadQueries(path), ct));
            }
            catch (InvalidOperationException ex)
            {
                // Raised when the configured provider can't produce a meaningful score.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict);
            }
        })
        .WithName("EvaluateRetrieval")
        .WithSummary("Scores retrieval against the fixed query set: hit@1/5/10 and MRR.");

        group.MapGet("/stats", async (IVectorStore vectors, IEmbeddingProvider embeddings, CancellationToken ct) =>
            Results.Ok(new
            {
                embeddingModel = embeddings.ModelId,
                dimensions = embeddings.Dimensions,
                vectorCount = await vectors.CountAsync(ct),
            }))
        .WithName("GetSearchStats")
        .WithSummary("Vector count and the embedding model currently configured.");

        return app;
    }
}
