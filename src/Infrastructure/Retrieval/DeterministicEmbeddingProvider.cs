using System.Security.Cryptography;
using System.Text;
using HilmaAgent.Core.Retrieval;
using Microsoft.Extensions.Options;

namespace HilmaAgent.Infrastructure.Retrieval;

/// <summary>
/// A hash-based stand-in for a real embedding model. Same text always yields the same vector, and
/// texts sharing tokens land nearer each other than unrelated ones — enough to exercise chunking,
/// upsert, filtering, and the search endpoint end to end with no key and no network.
/// </summary>
/// <remarks>
/// <b>This does not model meaning.</b> Synonyms and paraphrases are as far apart as unrelated text,
/// so retrieval *quality* cannot be judged in this mode — the evaluation harness refuses to score
/// against it. It exists so the stack runs for someone who has cloned the repo without an Azure key.
/// </remarks>
public class DeterministicEmbeddingProvider(IOptions<EmbeddingOptions> options) : IEmbeddingProvider
{
    private readonly EmbeddingOptions _options = options.Value;

    /// <summary>Recorded against every vector, and the marker the evaluation harness refuses to score.</summary>
    public const string ModelIdentifier = "deterministic-fake";

    public string ModelId => ModelIdentifier;

    public int Dimensions => _options.Dimensions;

    public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<float[]>>(texts.Select(Embed).ToList());

    private float[] Embed(string text)
    {
        var vector = new float[Dimensions];

        // A bag-of-tokens projection: each token contributes to a few fixed dimensions, so shared
        // vocabulary produces measurable overlap.
        foreach (var token in Tokenize(text))
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            for (var i = 0; i < 4; i++)
            {
                var index = BitConverter.ToUInt32(hash, i * 4) % (uint)Dimensions;
                var sign = (hash[16 + i] & 1) == 0 ? 1f : -1f;
                vector[index] += sign;
            }
        }

        var magnitude = MathF.Sqrt(vector.Sum(value => value * value));
        if (magnitude > 0)
            for (var i = 0; i < vector.Length; i++)
                vector[i] /= magnitude;
        else
            vector[0] = 1f; // A zero vector has no cosine similarity; give empty text a defined direction.

        return vector;
    }

    private static IEnumerable<string> Tokenize(string text) => text
        .ToLowerInvariant()
        .Split([' ', '\n', '\r', '\t', ',', '.', ':', ';', '(', ')', '/', '-', '"', '\''],
            StringSplitOptions.RemoveEmptyEntries)
        .Where(token => token.Length > 2);
}
