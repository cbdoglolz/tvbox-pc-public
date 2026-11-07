namespace TVBoxForWindows.Models;

public static class ResumeProgress
{
    // Native decoder time can briefly reset before a source replacement. Prefer the last verified
    // frame position in that case, while an explicit pending seek (including zero) always wins.
    public static long SourceSwitchPosition(long decoderMs, long verifiedMs, long displayedMs, long? pendingSeekMs)
    {
        if (pendingSeekMs.HasValue) return Math.Max(0, pendingSeekMs.Value);
        if (decoderMs + 5000 < verifiedMs && decoderMs + 5000 < displayedMs) return Math.Max(0, verifiedMs);
        return Math.Max(0, decoderMs);
    }
    public static bool Reached(long targetMs, long positionMs) => targetMs <= 0 || Math.Abs(positionMs - targetMs) <= 2500;
    public static bool Restored(long targetMs, long positionMs, bool newFrames, bool buffering, bool seeking) =>
        newFrames && !buffering && !seeking && positionMs >= targetMs + 250 && positionMs <= targetMs + 5000;
    public static bool CanCommit(bool frameRendered, bool replacingSource, bool awaitingResume) => frameRendered && !replacingSource && !awaitingResume;
}
