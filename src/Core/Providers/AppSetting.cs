namespace HilmaAgent.Core.Providers;

/// <summary>
/// A runtime setting that outlives a container restart, stored rather than configured.
/// </summary>
/// <remarks>
/// Deliberately a key/value pair rather than a typed table: the only things in here are choices the
/// operator makes through the UI, and each one that grows a column is a migration for something that
/// is really just a preference.
/// </remarks>
public class AppSetting
{
    public required string Key { get; set; }
    public string? Value { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public static class SettingKeys
{
    /// <summary>Which provider writes assessment narratives — see <see cref="ProviderKeys"/>.</summary>
    public const string NarratorProvider = "narrator.provider";
}
