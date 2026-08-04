namespace HilmaAgent.Core.Ingestion;

/// <summary>
/// Tracks how far ingestion has got, so a restart does not re-fetch everything.
/// Single row per source; <see cref="HilmaSource"/> is the only one today.
/// </summary>
public class IngestionCheckpoint
{
    public const string HilmaSource = "hilma";

    public required string Source { get; set; }

    /// <summary>Publication date of the newest notice successfully stored.</summary>
    public DateTimeOffset LastSeenPublicationDate { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
