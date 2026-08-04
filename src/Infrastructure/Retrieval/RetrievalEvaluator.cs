using System.Text.Json;
using HilmaAgent.Core.Retrieval;
using Microsoft.Extensions.Logging;

namespace HilmaAgent.Infrastructure.Retrieval;

/// <param name="Query">The Finnish query a bidder would actually type.</param>
/// <param name="ExpectedCpvPrefixes">A hit counts as relevant if the notice carries a CPV code with one of these prefixes.</param>
/// <param name="ExpectedNoticeIds">Optional hand-labelled ids, checked in addition to the CPV rule.</param>
public sealed record EvalQuery(
    string Query,
    IReadOnlyList<string> ExpectedCpvPrefixes,
    IReadOnlyList<string>? ExpectedNoticeIds = null);

public sealed record EvalQueryResult(
    string Query,
    bool HitAt1,
    bool HitAt5,
    bool HitAt10,
    double ReciprocalRank,
    int RelevantInTop10,
    IReadOnlyList<string> TopNoticeTitles);

/// <param name="HitRateAt5">Share of queries with at least one relevant notice in the top 5. The headline number.</param>
/// <param name="MeanReciprocalRank">How high the first relevant notice ranks, averaged. Rewards putting it first.</param>
public sealed record EvalReport(
    string EmbeddingModel,
    int QueryCount,
    double HitRateAt1,
    double HitRateAt5,
    double HitRateAt10,
    double MeanReciprocalRank,
    IReadOnlyList<EvalQueryResult> Results);

/// <summary>
/// Scores retrieval quality against a fixed query set — the checkpoint the project plan insists on
/// clearing before any LLM is added, because bad retrieval is otherwise misdiagnosed as a bad model.
/// </summary>
/// <remarks>
/// Relevance is judged by CPV prefix rather than hand-labelled ids. Hand-labelling would be more
/// precise, but it has to be redone every time the corpus changes; CPV is the procurement domain's
/// own taxonomy, so the judgement stays valid as notices come and go. Where a query needs a specific
/// notice, <see cref="EvalQuery.ExpectedNoticeIds"/> adds that on top.
/// </remarks>
public class RetrievalEvaluator(
    NoticeSearchService search,
    IEmbeddingProvider embeddings,
    ILogger<RetrievalEvaluator> logger)
{
    public async Task<EvalReport> EvaluateAsync(IReadOnlyList<EvalQuery> queries, CancellationToken ct = default)
    {
        if (embeddings.ModelId == DeterministicEmbeddingProvider.ModelIdentifier)
            throw new InvalidOperationException(
                "Retrieval evaluation is meaningless against the deterministic stand-in provider: it matches shared " +
                "tokens, not meaning, so the score would measure keyword overlap. Configure a real embedding provider.");

        var results = new List<EvalQueryResult>(queries.Count);

        foreach (var query in queries)
        {
            ct.ThrowIfCancellationRequested();

            var hits = await search.SearchAsync(query.Query, limit: 10, ct: ct);
            var relevance = hits.Select(hit => IsRelevant(hit, query)).ToList();
            var firstRelevant = relevance.IndexOf(true);

            results.Add(new EvalQueryResult(
                query.Query,
                HitAt1: firstRelevant == 0,
                HitAt5: firstRelevant >= 0 && firstRelevant < 5,
                HitAt10: firstRelevant >= 0,
                ReciprocalRank: firstRelevant >= 0 ? 1.0 / (firstRelevant + 1) : 0,
                RelevantInTop10: relevance.Count(r => r),
                TopNoticeTitles: hits.Take(3).Select(hit => hit.Title ?? hit.NoticeId).ToList()));
        }

        var report = new EvalReport(
            embeddings.ModelId,
            results.Count,
            Mean(results, r => r.HitAt1 ? 1 : 0),
            Mean(results, r => r.HitAt5 ? 1 : 0),
            Mean(results, r => r.HitAt10 ? 1 : 0),
            Mean(results, r => r.ReciprocalRank),
            results);

        logger.LogInformation(
            "Retrieval eval ({Model}): hit@1 {At1:P0}, hit@5 {At5:P0}, hit@10 {At10:P0}, MRR {Mrr:F3} over {Count} queries.",
            report.EmbeddingModel, report.HitRateAt1, report.HitRateAt5, report.HitRateAt10,
            report.MeanReciprocalRank, report.QueryCount);

        return report;
    }

    private static bool IsRelevant(NoticeSearchResult hit, EvalQuery query)
    {
        if (query.ExpectedNoticeIds?.Contains(hit.NoticeId) == true) return true;

        return query.ExpectedCpvPrefixes.Count > 0
            && hit.CpvCodes.Any(code => query.ExpectedCpvPrefixes.Any(prefix =>
                code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
    }

    private static double Mean(IReadOnlyList<EvalQueryResult> results, Func<EvalQueryResult, double> selector) =>
        results.Count == 0 ? 0 : results.Average(selector);

    /// <summary>Loads the query set shipped alongside the API.</summary>
    public static IReadOnlyList<EvalQuery> LoadQueries(string path) =>
        JsonSerializer.Deserialize<List<EvalQuery>>(File.ReadAllText(path),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new InvalidOperationException($"No eval queries found in {path}.");
}
