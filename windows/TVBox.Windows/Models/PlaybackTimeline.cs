namespace TVBoxForWindows.Models;

public record TimelineSegment(long Start, long Offset);

/// <summary>Maps discontinuous VOD decoder timestamps to a continuous playback position.</summary>
public sealed class PlaybackTimeline
{
    readonly object _sync = new();
    long _offset, _seekOffset, _lastRaw, _lastTick;
    List<TimelineSegment> _segments = new() { new(0, 0) };
    bool _initialized, _seeking;
    public int Corrections { get; private set; }
    public long Read(long raw) { lock (_sync) return Math.Max(0, raw + (_seeking ? _seekOffset : _offset)); }
    public void Reset() { lock (_sync) { _offset = _seekOffset = 0; _initialized = _seeking = false; Corrections = 0; _segments = new() { new(0, 0) }; } }
    public void BeginSeek() { lock (_sync) { _seekOffset = 0; _seeking = true; } }
    public long BeginSeek(long target)
    {
        lock (_sync)
        {
            var segment = _segments.Where(s => s.Start <= target).OrderByDescending(s => s.Start).FirstOrDefault() ?? new(0, 0);
            var rawTarget = Math.Max(0, target - segment.Offset);
            _seekOffset = target - rawTarget; _seeking = true;
            return rawTarget;
        }
    }
    public List<TimelineSegment> Snapshot() { lock (_sync) return _segments.ToList(); }
    public void Restore(List<TimelineSegment> segments) { lock (_sync) { if (segments?.Count > 0) _segments = segments.ToList(); _initialized = false; } }
    public void EndSeek(bool success) { lock (_sync) { if (success) _offset = _seekOffset; _seeking = false; _initialized = false; } }
    public long Observe(long raw, long tick, bool advancing, double speed)
    {
        lock (_sync)
        {
            raw = Math.Max(0, raw);
            if (_seeking) return Math.Max(0, raw + _seekOffset);
            var elapsed = tick - _lastTick;
            if (_initialized && elapsed is >= 0 and <= 5000)
            {
                var delta = raw - _lastRaw;
                var expected = advancing ? elapsed * Math.Clamp(speed, .25, 8) : 0;
                // A real seek is explicitly bracketed and never enters this branch. A long UI
                // suspension is re-anchored instead of assuming it was uninterrupted playback.
                if (Math.Abs(delta) > 5000 && Math.Abs(delta - expected) > 5000)
                {
                    _offset += (long)expected - delta; Corrections++;
                    var start = Math.Max(0, raw + _offset);
                    _segments.RemoveAll(s => s.Start > 0 && Math.Abs(s.Start - start) < 2000);
                    _segments.Add(new(start, _offset));
                    if (_segments.Count > 64) _segments.RemoveAt(1);
                }
            }
            _lastRaw = raw; _lastTick = tick; _initialized = true;
            return Math.Max(0, raw + _offset);
        }
    }
}
