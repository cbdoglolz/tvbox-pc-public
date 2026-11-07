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
    public static string Status => "已接入4KHDHub、AniZone、Vixsrc独立国际适配器；在站点页开关，需要可靠TMDB匹配。当前可用性以媒体检测与实际播放为准，小雅/G-Box/WebDAV尚未接入。";
}
