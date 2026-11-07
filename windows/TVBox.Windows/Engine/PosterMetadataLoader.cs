using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

namespace TVBoxForWindows.Engine;

/// <summary>Only the caller's loaded cards are hydrated; bounded network work, cancellable on channel change.</summary>
public sealed class PosterMetadataLoader(MetadataDetailsService details = null, Func<bool> enabled = null)
{
    static readonly SemaphoreSlim Slots = new(2);
    readonly MetadataDetailsService _details = details ?? new();
    readonly Func<bool> _enabled = enabled ?? (() => TmdbCredentials.HasCredential);
    readonly Dictionary<string, CancellationToken> _pending = new();
    readonly Dictionary<string, (CatalogItem Work, DateTimeOffset At)> _loaded = new();
    public async Task LoadAsync(IEnumerable<CatalogItem> cards, Action<CatalogItem, CatalogItem> apply, CancellationToken ct)
    {
        if (!_enabled()) return;
        await Task.WhenAll(cards.ToList().Select(async card =>
        {
            var key = card.WorkKey;
            if (details == null && CatalogArtworkResolver.Shared.ReadCorrection(card) is { } corrected) { if (!ct.IsCancellationRequested) apply(card, corrected); return; }
            if (_loaded.TryGetValue(key, out var loaded) && DateTimeOffset.UtcNow - loaded.At < TimeSpan.FromMinutes(20)) { if (!ct.IsCancellationRequested) apply(card, loaded.Work.Copy()); return; }
            if (key.Length == 0 || _pending.TryGetValue(key, out var previous) && !previous.IsCancellationRequested) return;
            _pending[key] = ct;
            try
            {
                await Slots.WaitAsync(ct);
                try
                {
                    var full = details == null ? await CatalogArtworkResolver.Shared.ResolveAsync(card, ct, refreshMetadata: true) : await _details.EnrichAsync(card, ct);
                    ct.ThrowIfCancellationRequested(); apply(card, full);
                    if (full.GetRating("tmdb").HasScore || full.ArtworkSchema >= 1) { if (_loaded.Count >= 128) _loaded.Clear(); _loaded[key] = (full.Copy(), DateTimeOffset.UtcNow); }
                }
                finally { Slots.Release(); }
            }
            catch (OperationCanceledException) { }
            catch { /* Cards and ordinary playback remain usable while metadata is unavailable. */ }
            finally { if (_pending.TryGetValue(key, out var owner) && owner == ct) _pending.Remove(key); }
        }));
    }
}
