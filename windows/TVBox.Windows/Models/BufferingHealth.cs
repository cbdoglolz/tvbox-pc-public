namespace TVBoxForWindows.Models;

/// <summary>Monotonic-time buffer observations; excludes explicit seek/source transitions in caller.</summary>
public sealed class BufferingHealth
{
    readonly Queue<long> _recent = new();
    long? _started;
    public int Count { get; private set; }
    public long TotalMs { get; private set; }
    public void Reset() { _recent.Clear(); _started = null; Count = 0; TotalMs = 0; }
    public bool Observe(bool buffering, long tick)
    {
        while (_recent.TryPeek(out var oldest) && tick - oldest > 60000) _recent.Dequeue();
        if (buffering) { _started ??= tick; return false; }
        if (_started is not long start) return false;
        var duration = Math.Max(0, tick - start); _started = null;
        // Decoder state transitions around HLS discontinuities are often one frame.
        // Count only interruptions long enough to be perceptible.
        if (duration < 250) return false;
        TotalMs += duration; Count++; _recent.Enqueue(tick);
        return duration >= 8000 || _recent.Count >= 3;
    }
}
