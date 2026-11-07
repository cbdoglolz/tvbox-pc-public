using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

/// <summary>Resolve artwork before its first display. Concurrent posters, title and banner share one request.</summary>
public sealed class CatalogArtworkResolver(Func<CatalogItem, CancellationToken, Task<CatalogItem>> fetch = null, Func<bool> enabled = null, MetadataStore store = null)
{
    public static CatalogArtworkResolver Shared { get; } = new();
    readonly Func<CatalogItem, CancellationToken, Task<CatalogItem>> _fetch = fetch ?? new MetadataDetailsService().EnrichAsync;
    readonly Func<bool> _enabled = enabled ?? (() => TmdbCredentials.HasCredential);
    MetadataStore Store => store ?? MetadataStore.Instance;
    bool Persistent => fetch == null || store != null;
    readonly ArtworkRequestGate _slots = new(2);
    readonly Dictionary<string, Flight> _pending = new();
    readonly Dictionary<string, CatalogItem> _corrections = new();
    public void RememberCorrection(CatalogItem work)
    {
        var copy = work.Copy(); copy.ArtworkResolved = true;
        lock (_corrections) { if (_corrections.Count >= 64) _corrections.Clear(); _corrections[work.WorkKey] = copy; }
        if (Persistent) Store.Write("preferred-artwork-v1", work.WorkKey, copy, TimeSpan.FromDays(30));
    }
    public CatalogItem ReadCorrection(CatalogItem work)
    { lock (_corrections) return _corrections.TryGetValue(work.WorkKey, out var corrected) ? corrected.Copy() : null; }
    sealed class Flight { public CancellationTokenSource Cts = new(); public Task<CatalogItem> Task; public int Readers; }
    public async Task<CatalogItem> ResolveAsync(CatalogItem work, CancellationToken ct, bool refreshMetadata = false, bool priority = false)
    {
        ct.ThrowIfCancellationRequested();
        if (work == null) return null;
        if (ReadCorrection(work) is { } corrected) return corrected;
        if (!refreshMetadata && Persistent && work.WorkKey.Length > 0) {
            var cached = Store.Read<CatalogItem>("preferred-artwork-v1", work.WorkKey);
            var previous = cached?.IsFresh == true ? cached.Value : Store.Read<CatalogItem>("work-detail", work.WorkKey)?.Value;
            var mapping = Store.Read<MetadataLink>("mapping", work.WorkKey)?.Value;
            if (previous != null && (cached?.IsFresh == true || previous.ArtworkSchema >= 2) && (mapping == null || mapping.Suppressed == previous.TmdbLinkSuppressed && mapping.Ids.Tmdb == previous.ExternalIds.Tmdb)) {
                var ready = previous.Copy(); ready.ArtworkResolved = true; return ready;
            }
        }
        if (!_enabled() || !refreshMetadata && (work.ArtworkResolved || work.ArtworkSchema >= 2 && work.DetailsSchema >= 8)) return work.Copy();
        var key = (work.WorkKey.Length > 0 ? work.WorkKey : work.Title + "|" + work.Year + "|" + work.Kind) + "|" + work.ExternalIds.Tmdb + "|" + work.TmdbLinkSuppressed;
        Flight flight;
        lock (_pending) {
            if (!_pending.TryGetValue(key, out flight) || flight.Cts.IsCancellationRequested) {
                flight = new(); _pending[key] = flight;
                flight.Task = FetchAsync(work.Copy(), flight, priority);
            }
            else if (priority) _slots.Promote(flight);
            flight.Readers++;
        }
        try {
            var resolved = (await flight.Task.WaitAsync(ct)).Copy(); resolved.ArtworkResolved = true;
            if (Persistent && work.WorkKey.Length > 0 && resolved.Poster.Length > 0) Store.Write("preferred-artwork-v1", work.WorkKey, resolved, resolved.PosterLanguage == "zh" ? TimeSpan.FromDays(30) : resolved.ArtworkSchema >= 2 ? TimeSpan.FromDays(3) : TimeSpan.FromMinutes(15));
            return resolved;
        }
        catch (Exception) when (!ct.IsCancellationRequested) { var fallback = work.Copy(); fallback.ArtworkResolved = true; return fallback; }
        finally {
            lock (_pending) {
                if (--flight.Readers == 0) {
                    if (_pending.TryGetValue(key, out var current) && ReferenceEquals(current, flight)) _pending.Remove(key);
                    if (!flight.Task.IsCompleted) flight.Cts.Cancel();
                    _ = flight.Task.ContinueWith(done => { _ = done.Exception; flight.Cts.Dispose(); }, TaskScheduler.Default);
                }
            }
        }
    }
    async Task<CatalogItem> FetchAsync(CatalogItem work, Flight flight, bool priority)
    {
        var ct = flight.Cts.Token;
        await _slots.EnterAsync(flight, priority, ct);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try { return await _fetch(work, timeout.Token); }
        finally { _slots.Exit(); }
    }
}
