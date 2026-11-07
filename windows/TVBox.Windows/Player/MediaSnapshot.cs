namespace TVBoxForWindows.Player;

/// <summary>Codec parameters, not network throughput. Missing fields remain unknown.</summary>
public record MediaSnapshot(int Width, int Height, string Codec, string Container, double Fps, long Bitrate, long DurationMs, long ExactBytes = 0, bool Sampled = false)
{
    public string Rate => Bitrate > 0 ? $"{Bitrate / 1_000_000d:0.00} Mb/s{(Sampled ? "（播放采样）" : "")}" : "码率未公布";
    public string Summary => $"{(Width > 0 && Height > 0 ? $"{Width}×{Height}" : "分辨率待解析")} · {Codec} · {Rate} · {Container}";
    public string SizeLabel => ExactBytes > 0 ? $"{ExactBytes / 1_073_741_824d:0.00} GiB（文件大小）" :
        Bitrate > 0 && DurationMs > 0 ? $"约 {Bitrate / 8d * DurationMs / 1000 / 1_073_741_824d:0.00} GiB（码率估算，非精确文件大小）" : "流媒体未公布";
}

public sealed class MediaRateMemory
{
    int _generation = -1;
    long _declared, _sample;
    public (long Rate, bool Sampled) Observe(int generation, long declaredBits, long sampledBits, bool playing)
    {
        if (_generation != generation) { _generation = generation; _declared = _sample = 0; }
        if (declaredBits > 0) _declared = declaredBits;
        if (playing && sampledBits > 0) _sample = sampledBits;
        return _declared > 0 ? (_declared, false) : (_sample, _sample > 0);
    }
}
