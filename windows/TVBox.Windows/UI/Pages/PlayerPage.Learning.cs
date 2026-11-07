using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;

namespace TVBoxForWindows.UI.Pages;

public sealed partial class PlayerPage
{
    long _sourceAttemptStarted;
    bool _sourceFailureRecorded, _sourceFrameRecorded;
    int _sourceRecordedHeight;
    readonly HashSet<string> _wonEpisodes = new();
    void BeginSourceAttempt()
    {
        _sourceAttemptStarted = Environment.TickCount64; _sourceFailureRecorded = _sourceFrameRecorded = false; _sourceRecordedHeight = 0;
        var site = _session?.CurrentSite;
        if (site != null) _ = Task.Run(() => SourceLearning.Record(site.Key, site.Name, "attempt", ""));
    }
    void RecordSourceFailure()
    {
        if (_sourceFailureRecorded) return; _sourceFailureRecorded = true;
        var site = _session?.CurrentSite; var work = _session?.Catalog; var episode = _session?.CurrentEpisode?.Name; var cid = VodConfigService.Cid;
        if (site == null) return;
        _ = Task.Run(() => { SourceLearning.Record(site.Key, site.Name, "failure", ""); if (work != null) SourceLearning.Remember(work, episode, cid, site.Key, false); });
    }
    void RecordSourceSuccess()
    {
        if (_sourceFailureRecorded) return;
        var site = _session?.CurrentSite; var work = _session?.Catalog; var episode = _session?.CurrentEpisode?.Name ?? ""; var cid = VodConfigService.Cid;
        if (site == null) return;
        if (_sourceFrameRecorded) {
            if (_sourceRecordedHeight <= 0 && (_sourceRecordedHeight = _core.ReadMediaSnapshot().Height) > 0) { var observed = _sourceRecordedHeight; _ = Task.Run(() => SourceLearning.ObserveHeight(site.Key, observed)); }
            return;
        }
        _sourceFrameRecorded = true;
        var first = _wonEpisodes.Add(SourceLearning.EpisodeIdentity(work?.Kind ?? TitleMatchRules.GuessKind(_session.Vod.TypeName), episode)); var elapsed = Environment.TickCount64 - _sourceAttemptStarted;
        var height = _sourceRecordedHeight = _core.ReadMediaSnapshot().Height; var preference = _appliedPreference ?? Setting.SourcePreference;
        _ = Task.Run(() => { if (work != null) SourceLearning.Remember(work, episode, cid, site.Key, true); if (first) SourceLearning.Record(site.Key, site.Name, "win", preference, elapsed, height); });
    }
}
