namespace HilmaAgent.Core.Notices;

/// <summary>
/// Maps a raw Hilma detail document onto <see cref="Notice"/>.
/// </summary>
/// <remarks>
/// Finnish notices are migrating from a legacy shape to eForms. Which shape(s) we support is an
/// open decision, deferred until we can inspect live responses — see README. Until then the
/// implementation is deliberately tolerant: unknown or missing fields leave properties null and
/// the full payload is preserved on <see cref="Notice.RawPayload"/>.
/// </remarks>
public interface INoticeParser
{
    Notice Parse(HilmaNoticeDocument document);
}
