namespace TVBoxForWindows.Models;

public static class ResumeProgress
{
    public static bool Reached(long targetMs, long positionMs) => targetMs <= 0 || Math.Abs(positionMs - targetMs) <= 2500;
    public static bool Restored(long targetMs, long positionMs, bool newFrames, bool buffering, bool seeking) =>
        newFrames && !buffering && !seeking && positionMs >= targetMs + 250 && positionMs <= targetMs + 5000;
    public static bool CanCommit(bool frameRendered, bool replacingSource, bool awaitingResume) => frameRendered && !replacingSource && !awaitingResume;
}
