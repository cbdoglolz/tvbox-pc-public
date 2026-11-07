using TVBoxForWindows.Engine;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;

internal static class CatalogV085Regression
{
    public static async Task RunAsync(Action<bool, string> require)
    {
        var gate = new ArtworkRequestGate(1); var active = new object(); var wall = new object(); var hero = new object();
        await gate.EnterAsync(active, false, default);
        var wallTask = gate.EnterAsync(wall, false, default); var heroTask = gate.EnterAsync(hero, true, default);
        require(!wallTask.IsCompleted && !heroTask.IsCompleted, "v085 foreground requests preserve the shared concurrency limit");
        gate.Exit(); await heroTask.WaitAsync(TimeSpan.FromSeconds(1));
        require(!wallTask.IsCompleted, "v085 next hero artwork runs before queued poster-wall work");
        gate.Exit(); await wallTask.WaitAsync(TimeSpan.FromSeconds(1)); gate.Exit();
        await gate.EnterAsync(active, false, default);
        var first = gate.EnterAsync(wall, false, default); var promoted = gate.EnterAsync(hero, false, default); gate.Promote(hero); gate.Exit();
        await promoted.WaitAsync(TimeSpan.FromSeconds(1));
        require(!first.IsCompleted, "v085 a shared queued poster request can be promoted when the banner needs the same title");
        gate.Exit(); await first; gate.Exit();
        await gate.EnterAsync(active, false, default); using var cancelled = new CancellationTokenSource();
        var abandoned = gate.EnterAsync(hero, true, cancelled.Token); cancelled.Cancel();
        var cancelledCorrectly = false; try { await abandoned; } catch (OperationCanceledException) { cancelledCorrectly = true; }
        var remaining = gate.EnterAsync(wall, false, default); gate.Exit(); await remaining.WaitAsync(TimeSpan.FromSeconds(1)); gate.Exit();
        require(cancelledCorrectly, "v085 abandoned foreground artwork leaves the queue and cannot consume a released slot");
        var series = new CatalogItem { Provider = "tmdb", Id = "95842", Title = "庆余年", Kind = "tv", Year = "2019", SelectedSeason = 1 };
        var queries = CatalogIdentity.PlaybackSearchNames(series).ToList();
        require(queries.First() == "庆余年 第一季" && queries.Contains("庆余年") && queries.Count <= 3, "v085 source search uses Chinese season wording and bounded base-title fallback instead of only an Arabic ordinal");
        series.SelectedSeason = 2; series.Year = "2024";
        require(CatalogIdentity.PlaybackSearchNames(series).First() == "庆余年 第二季" && !TitleMatchRules.Matches(CatalogIdentity.PlaybackNames(series).First(), series.Year, "tv", "庆余年第一季", "2019", "国产剧"), "v085 broad lookup never relaxes the strict season and year acceptance check");
        series.SelectedSeason = 12;
        require(CatalogIdentity.PlaybackSearchNames(series).First().EndsWith("第十二季"), "v085 season twelve uses the correct Chinese ordinal");
        var movie = new CatalogItem { Title = "肖申克的救赎", Kind = "movie", OriginalTitle = "The Shawshank Redemption" };
        require(CatalogIdentity.PlaybackSearchNames(movie).First() == movie.Title && CatalogIdentity.PlaybackSearchNames(movie).Contains(movie.OriginalTitle), "v085 movie and alternative-title search terms remain intact");
        var oldKey = "77|" + TitleMatchRules.Normalize(series.Title) + "|" + series.Year + "|tv|2";
        var legacy = Path.Combine(AppPaths.Cache, "source-health", Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(oldKey))) + ".json");
        DurableJsonFile.Write(legacy, JsonUtil.Serialize(new Dictionary<string, SourceHealth> { ["good"] = new(true, DateTimeOffset.UtcNow), ["bad"] = new(false, DateTimeOffset.UtcNow) }));
        var health = SourceLearning.Health(series, "第02集", 77);
        require(health.ContainsKey("good") && !health.ContainsKey("bad"), "v085 corrected source-query policy preserves proven sources while bypassing obsolete negative records");
        SourceLearning.Remember(series, "第02集", 77, "new-bad", false);
        require(SourceLearning.Health(series, "第02集", 77).ContainsKey("new-bad"), "v085 newly confirmed failures remain cached under the corrected policy");
        var special = series.Copy(); special.SelectedSeason = 0;
        require(!SourceLearning.Health(special, "第02集", 77).ContainsKey("new-bad"), "v085 source health also isolates explicit seasons and specials");
    }
}
