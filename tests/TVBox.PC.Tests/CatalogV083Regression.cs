using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;

internal static class CatalogV083Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        using var doc = JsonDocument.Parse("""{"id":88,"title":"中文片名","poster_path":"/original.jpg","backdrop_path":"/fallback-bg.jpg","images":{"posters":[{"file_path":"/zh.jpg","iso_639_1":"zh"}],"logos":[{"file_path":"/en.png","iso_639_1":"en","vote_average":10},{"file_path":"/zh.png","iso_639_1":"zh","vote_average":4}],"backdrops":[{"file_path":"/english-bg.jpg","iso_639_1":"en","vote_count":999},{"file_path":"/clean-bg.jpg","iso_639_1":null,"vote_count":1}]},"credits":{"crew":[{"id":20,"name":"导演甲","job":"Director","profile_path":"/director.jpg"},{"id":30,"name":"编剧","job":"Writer"}],"cast":[{"id":10,"name":"演员乙","character":"角色丙"}]}}""");
        var work = CatalogMetadataParser.TmdbItem(doc.RootElement, "movie");
        require(work.Poster.EndsWith("/zh.jpg") && work.PosterFallback.EndsWith("/original.jpg"), "v083 Chinese poster is selected before display and original remains a download-failure fallback");
        require(work.TitleLogo.EndsWith("/zh.png") && work.TitleLogoLanguage == "zh", "v083 Chinese title artwork outranks higher-rated English logo");
        require(work.Backdrop.EndsWith("/clean-bg.jpg"), "v083 banner prefers text-free landscape artwork over foreign-language lettering");
        require(work.ArtworkSchema == 2 && work.DetailsSchema == 8, "v083 old artwork and people caches are upgraded additively");
        require(work.Crew.Single().Id == "20" && work.Crew[0].Job == "导演" && work.Crew[0].Portrait.EndsWith("/director.jpg"), "v083 directors retain reliable person IDs, portraits and localized role");
        var copy = new CatalogItem(); copy.ApplyMetadata(work);
        require(copy.TitleLogo == work.TitleLogo && copy.Backdrop == work.Backdrop && copy.Crew.Single().Id == "20", "v083 metadata updates preserve title, banner and director data");
        copy.ApplyMetadata(new CatalogItem { Poster = "https://fixture.test/late-list-poster.jpg" });
        require(copy.TitleLogo == work.TitleLogo && copy.Backdrop == work.Backdrop && copy.Crew.Single().Id == "20", "v083 late lightweight catalogue rows cannot erase resolved title artwork, backdrop or director metadata");
        using var invalid = JsonDocument.Parse("""{"images":{"logos":[{"file_path":"https://bad.test/remote.png","iso_639_1":"zh"},{"file_path":"/../secret","iso_639_1":"zh"}]},"backdrop_path":"//bad.test/banner"}""");
        require(PosterPreference.ChooseLogo(invalid.RootElement).Path == "", "v083 logo metadata cannot inject arbitrary hosts or traversal paths");
        require(PosterPreference.ChooseBackdrop(invalid.RootElement) == "", "v083 backdrop metadata rejects protocol-relative external hosts");
        using var english = JsonDocument.Parse("""{"images":{"logos":[{"file_path":"/english.png","iso_639_1":"en"}]}}""");
        require(PosterPreference.ChooseLogo(english.RootElement).Path == "/english.png", "v083 missing Chinese title art permits another language only after artwork selection");
        using var empty = JsonDocument.Parse("{}");
        require(PosterPreference.ChooseLogo(empty.RootElement).Path == "" && PosterPreference.ChooseBackdrop(empty.RootElement) == "", "v083 absent artwork has an explicit empty result for one-time text or gradient fallback");
        var credits = """{"cast":[{"id":1,"media_type":"movie","title":"演出作品"}],"crew":[{"id":2,"media_type":"movie","title":"执导作品","job":"Director"},{"id":1,"media_type":"movie","title":"演出作品","job":"Director"},{"id":3,"media_type":"movie","title":"未选择编剧作品","job":"Writer"}]}""";
        var related = PersonCatalog.ParseCredits(credits, "movie");
        require(related.Count == 2 && related.Any(w => w.Id == "2"), "v083 director filmography includes directed work and deduplicates acting/directing overlap");

        var ready = new TaskCompletionSource<CatalogItem>(TaskCreationOptions.RunContinuationsAsynchronously); var started = 0;
        var resolver = new CatalogArtworkResolver((_, _) => { Interlocked.Increment(ref started); return ready.Task; }, () => true);
        var original = new CatalogItem { Id = "88", Title = "待决作品", Poster = "https://fixture.test/foreign.jpg" };
        using var abandoned = new CancellationTokenSource();
        var first = resolver.ResolveAsync(original, abandoned.Token); var second = resolver.ResolveAsync(original.Copy(), default);
        require(!first.IsCompleted && !second.IsCompleted && started == 1, "v083 initial artwork remains unresolved instead of returning a foreign poster before Chinese metadata");
        abandoned.Cancel(); var cancelled = false;
        try { await first; } catch (OperationCanceledException) { cancelled = true; }
        ready.SetResult(work);
        var resolved = await second;
        require(cancelled && resolved.Poster.EndsWith("/zh.jpg") && started == 1, "v083 one abandoned view cannot cancel another poster or banner sharing the same request");
        resolved.Title = "changed copy";
        require(work.Title != resolved.Title, "v083 shared artwork consumers receive independent snapshots");
        var calls = 0;
        resolver = new CatalogArtworkResolver((w, _) => { calls++; return Task.FromResult(w); }, () => true);
        await resolver.ResolveAsync(work, default);
        require(calls == 0, "v083 fully resolved warm artwork does not repeat metadata queries");
        resolver = new CatalogArtworkResolver((w, _) => { calls++; return Task.FromResult(w); }, () => false);
        resolved = await resolver.ResolveAsync(original, default);
        require(calls == 0 && resolved.Poster == original.Poster, "v083 optional TMDB without a key uses the known poster without blocking ordinary browsing");
        var failures = 0;
        resolver = new CatalogArtworkResolver((_, _) => { failures++; return Task.FromException<CatalogItem>(new IOException("fixture outage")); }, () => true);
        resolved = await resolver.ResolveAsync(original, default);
        require(resolved.Poster == original.Poster, "v083 failed preferred-artwork resolution releases the original fallback once");
        await resolver.ResolveAsync(resolved, default);
        require(failures == 1 && resolved.ArtworkResolved, "v083 a completed fallback decision cannot start another request while the same title and poster render");
        using var stopped = new CancellationTokenSource(); stopped.Cancel(); cancelled = false;
        try { await resolver.ResolveAsync(original, stopped.Token); } catch (OperationCanceledException) { cancelled = true; }
        require(cancelled, "v083 cancelled navigation cannot commit fallback artwork");
        var service = new MetadataDetailsService(store: new MetadataStore(Path.Combine(AppPaths.Root, "v083-map.sqlite")));
        var linked = service.Associate(new CatalogItem { Id = "101", Title = "豆瓣作品", Kind = "movie", Poster = "https://fixture.test/douban.jpg" }, work);
        var unlinked = service.Unlink(linked);
        require(unlinked.WorkKey == linked.WorkKey && unlinked.TmdbLinkSuppressed && unlinked.ExternalIds.Tmdb == null, "v083 clearing a wrong match preserves original work identity and suppresses automatic reattachment");
        require(unlinked.TitleLogo == "" && unlinked.Backdrop == "" && unlinked.Crew.All(p => p.Provider != "tmdb") && unlinked.Poster.EndsWith("/douban.jpg"), "v083 clearing a match also removes its incorrect title, backdrop, people and preferred poster");
        var rebound = service.Associate(unlinked, work);
        require(!rebound.TmdbLinkSuppressed && rebound.ExternalIds.Tmdb == 88 && rebound.TitleLogo == work.TitleLogo, "v083 explicit rebinding restores correct metadata after a suppressed match");
        resolver.RememberCorrection(unlinked);
        var corrected = await resolver.ResolveAsync(linked, default);
        require(corrected.TmdbLinkSuppressed && corrected.TitleLogo == "", "v083 manual correction overrides an already-resolved stale page snapshot");
        resolver.RememberCorrection(rebound);
        corrected = await resolver.ResolveAsync(unlinked, default);
        require(corrected.ExternalIds.Tmdb == 88 && corrected.TitleLogo == work.TitleLogo, "v083 returning to another page uses the latest explicit binding instead of its old artwork");
        var startedTwo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var busy = 0;
        resolver = new CatalogArtworkResolver(async (w, token) => {
            if (w.Id == "foreground") return w;
            if (Interlocked.Increment(ref busy) == 2) startedTwo.SetResult();
            await Task.Delay(Timeout.Infinite, token); return w;
        }, () => true);
        using var hiddenPage = new CancellationTokenSource();
        var background = Enumerable.Range(1, 3).Select(n => resolver.ResolveAsync(new CatalogItem { Id = n.ToString() }, hiddenPage.Token)).ToArray();
        await startedTwo.Task.WaitAsync(TimeSpan.FromSeconds(2)); hiddenPage.Cancel();
        var foreground = await resolver.ResolveAsync(new CatalogItem { Id = "foreground" }, default).WaitAsync(TimeSpan.FromSeconds(2));
        foreach (var pending in background) try { await pending; } catch (OperationCanceledException) { }
        require(foreground.Id == "foreground", "v083 cancelling all hidden-page readers releases active and queued artwork slots for the foreground page");
    }
}
