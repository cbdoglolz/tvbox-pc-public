namespace TVBoxForWindows.Models;

public enum QualityEvidence { Unknown, FilenameOnly, TrustedMetadata, Probed }
public record HdSourceCandidate(string ProviderId, string FileId, string Filename, long? SizeBytes, string Resolution, string Codec, string Hdr, string Audio,
    QualityEvidence Evidence, bool AuthorizationRequired, bool DirectPlaySupported, DateTimeOffset? ExpiresAt, int? Season = null, int? Episode = null);
public interface IHdSourceAdapter
{
    string Id { get; }
    bool CanSearch { get; }
    bool CanResolvePlayable { get; }
    bool CanTransfer { get; }
    Task<IReadOnlyList<HdSourceCandidate>> SearchAsync(CatalogItem work, CancellationToken ct);
    Task<string> ResolvePlayableAsync(HdSourceCandidate candidate, CancellationToken ct);
}
public static class HdSourceRegistry
{
    public static IReadOnlyList<IHdSourceAdapter> Adapters { get; } = Array.Empty<IHdSourceAdapter>();
    public static string Status => "尚未接入高清云端来源。小雅/G-Box适配安排在后续版本；当前普通播放和线路切换可正常使用。";
}
