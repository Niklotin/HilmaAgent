using System.ComponentModel.DataAnnotations;

namespace HilmaAgent.Infrastructure.Assessments;

public class GeminiOptions
{
    public const string SectionName = "Gemini";

    /// <summary>API key from Google AI Studio. user-secrets or environment variables — never committed.</summary>
    public string? ApiKey { get; set; }

    public string BaseUrl { get; set; } = "https://generativelanguage.googleapis.com/v1beta";

    /// <summary>
    /// Pinned rather than an alias such as <c>gemini-flash-latest</c>: an assessment records the model
    /// that wrote it, and an alias that shifts underneath would make stored assessments unreproducible.
    /// </summary>
    [Required]
    public string Model { get; set; } = "gemini-3.6-flash";

    /// <summary>How many retrieved passages the model may cite. More context, more tokens, more to ignore.</summary>
    [Range(1, 50)]
    public int MaxSources { get; set; } = 8;

    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(90);
}
