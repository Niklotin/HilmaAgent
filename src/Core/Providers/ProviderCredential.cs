namespace HilmaAgent.Core.Providers;

/// <summary>
/// Credentials and endpoint for one model provider, configured at runtime rather than at deploy time.
/// </summary>
/// <remarks>
/// <para>The API key is stored <b>encrypted</b> and is never sent back out. Reads return
/// <see cref="KeyHint"/> — the last four characters — which is enough to tell two keys apart and
/// useless to anyone who intercepts it. A key that cannot be read back cannot be leaked by the thing
/// that stores it.</para>
/// <para>Nothing here is secret to the *database owner*: encryption at rest protects against a
/// leaked dump or a stray backup, not against someone who already has both the database and the
/// application's data-protection keys. It raises the cost of a mistake; it does not replace a secret
/// manager, and the README says so.</para>
/// </remarks>
public class ProviderCredential
{
    /// <summary>Provider identifier — see <c>ProviderKeys</c>. One row per provider.</summary>
    public required string Provider { get; set; }

    /// <summary>
    /// The API key, encrypted with the application's data-protection key ring. Null for providers
    /// that need none, which is the normal case for a model running on your own machine.
    /// </summary>
    public string? ProtectedApiKey { get; set; }

    /// <summary>Last four characters of the key, so the UI can show *which* key without showing it.</summary>
    public string? KeyHint { get; set; }

    /// <summary>
    /// Endpoint override. This is what makes a local model work — point it at Ollama or LM Studio and
    /// the OpenAI-compatible provider talks to it unchanged.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// Model identifier to request. Pinned rather than an alias: an assessment records the model that
    /// wrote it, and an alias shifting underneath would make stored assessments unreproducible.
    /// </summary>
    public string? Model { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Who last changed it. A label, not an identity — there is still no auth.</summary>
    public string? UpdatedBy { get; set; }
}

/// <summary>Providers the narrator registry knows how to build.</summary>
public static class ProviderKeys
{
    /// <summary>Google Gemini. The default, and what every stored assessment so far was written by.</summary>
    public const string Gemini = "gemini";

    /// <summary>
    /// Anything speaking the OpenAI chat-completions API: OpenAI itself, Azure OpenAI, OpenRouter,
    /// and — the reason this exists — Ollama, LM Studio or vLLM running locally.
    /// </summary>
    public const string OpenAiCompatible = "openai-compatible";

    public static readonly string[] All = [Gemini, OpenAiCompatible];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}
