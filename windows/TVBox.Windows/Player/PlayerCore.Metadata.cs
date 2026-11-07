namespace TVBoxForWindows.Player;

public partial class PlayerCore
{
    readonly MediaRateMemory _mediaRateMemory = new();
    public MediaSnapshot ReadMediaSnapshot()
    {
        if (!HasRenderedVideoFrame) return new(0, 0, "待解析", "待解析", 0, 0, 0);
        try
        {
        var video = Fly.Video;
        var codec = string.IsNullOrEmpty(video.Codec) ? "编码未公布" : video.Codec;
        var format = Fly.VideoDemuxer?.Name;
        if (string.IsNullOrWhiteSpace(format)) format = (_item?.Url ?? "").Contains(".m3u8", StringComparison.OrdinalIgnoreCase) ? "HLS" :
            (_item?.Url ?? "").Contains(".mp4", StringComparison.OrdinalIgnoreCase) ? "MP4" : "容器未公布";
        // Flyleaf Video.BitRate / Audio.BitRate are Kbps, not bytes per second.
        var declaredVideo = Fly.VideoDemuxer?.VideoStream?.BitRate ?? 0;
        var declared = declaredVideo > 0 ? declaredVideo + (Fly.AudioDemuxer?.AudioStream?.BitRate ?? 0) : 0;
        var sample = (long)((video.BitRate + (Fly.Audio?.BitRate ?? 0)) * 1000);
        var rate = _mediaRateMemory.Observe(_openGeneration, declared, sample, IsPlaying);
        long size = 0;
        if (Uri.TryCreate(_item?.Url, UriKind.Absolute, out var uri) && uri.IsFile)
            try { size = new FileInfo(uri.LocalPath).Length; } catch { }
        return new(video.Width, video.Height, codec, format, video.FPS, rate.Rate, DurationMs, size, rate.Sampled);
        }
        catch { return new(0, 0, "待解析", "待解析", 0, 0, 0); }
    }
}
