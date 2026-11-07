using System.Diagnostics;
using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Engine;
using TVBoxForWindows.Models;
using TVBoxForWindows.Player;

if (args.Length > 0 && args[0] == "verify-artwork-restart") {
    AppPaths.Init(); Setting.Load();
    var resolver = new TVBoxForWindows.Engine.CatalogArtworkResolver((_, _) => throw new Exception("Unexpected metadata request after restart"), () => true, new MetadataStore(Path.Combine(AppPaths.Root, "v084", "metadata.sqlite")));
    var ready = await resolver.ResolveAsync(new CatalogItem { Id = "84", Title = "持久缓存" }, default);
    Require(ready.Poster.EndsWith("/rebound.jpg"), "v084 fresh process restores preferred poster from disk");
    Require(await ImageByteCache.ReadAsync(Path.Combine(AppPaths.Root, "v084"), "https://fixture.test/image") != null, "v084 fresh process restores decoded-image cache bytes from disk");
    Logger.Shutdown(); return;
}

// Separate processes exercise real disk reloads, not a static in-memory cache.
if (args.Length > 0 && args[0] == "verify-restart")
{
    AppPaths.Init();
    Setting.Load();
    Require(BuiltInSources.ResolveStartup().Url == BuiltInSources.ConfigUrl, "restart source");
    Require(Stores.FindKeep(1, "builtin_360@movie")?.VodName == "测试电影", "restart favorite");
    Require(Stores.FindHistory(1, "builtin_360@series")?.Position == 123456, "restart progress");
    Require(Stores.FindHistory(1, "builtin_360@series")?.EpisodeUrl == "episode-2", "restart episode");
    Require(Stores.FindHistory(1, "douban@123")?.SourceKey == "builtin_mdzy", "restart remembers last successful provider separately from Douban identity");
    Require(Stores.FindHistory(1, "douban@123")?.CatalogYear == "2026", "restart retains catalogue matching metadata");
    Require(Stores.FindKeep(1, "douban@123")?.CatalogKind == "tv", "restart restores Douban favorite identity");
    Logger.Shutdown();
    return;
}

var root = Path.Combine(Path.GetTempPath(), "tvbox-pc-tests-" + Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable("TVBOX_PC_DATA_DIR", root);
AppPaths.Init(); Setting.Load();
if (args.Contains("--rich-detail-live")) {
    var credentialFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TVBox PC", "tmdb.credential");
    if (!File.Exists(credentialFile)) throw new Exception("No configured credential for rich detail check");
    File.Copy(credentialFile, Path.Combine(root, "tmdb.credential"));
    var http = new TVBoxForWindows.Engine.MetadataHttp(new HttpClient());
    var provider = new TVBoxForWindows.Engine.CatalogQueryProvider(http);
    var page = await provider.FetchAsync(new CatalogQuery { Provider = "tmdb", Category = "movie", Keyword = "吸血鬼猎人" }, "movie", 1, "", default);
    var film = page.Items.First(w => w.Year == "1985");
    film = await provider.DetailsAsync(film, default);
    Require(film.Kind == "movie" && film.Year == "1985" && film.Companies.Count > 0 && film.Backdrop.Length > 0, "live 1985 Vampire Hunter D has real movie backdrop and production data");
    var media = new TVBoxForWindows.Engine.WorkMediaService(http);
    var collection = await media.CollectionAsync(film, default);
    Console.WriteLine($"Film={film.Id}; year={film.Year}; synopsisChinese={TVBoxForWindows.Engine.LocalizedSynopsis.Chinese(film.Overview).Length}; logos={film.Companies.Count(c => c.Logo.Length > 0)}; collection={collection.Count}; artLanguage={film.TitleLogoLanguage}");
    var series = await provider.DetailsAsync(new CatalogItem { Provider = "tmdb", Id = "1396", Kind = "tv" }, default);
    var episodes = await media.SeasonAsync(series, 1, default);
    Require(series.Seasons.Count > 1 && episodes.Count > 0 && episodes[0].Still.Length > 0 && episodes[0].Rating > 0, "live series season episodes contain still synopsis and TMDB episode score");
    var rec = new TVBoxForWindows.Engine.WorkRecommendations(store: MetadataStore.Instance, http: http);
    var similar = await rec.MoreAsync(film, new(), default);
    Require(similar.Items.Count > 0 && similar.Items.All(w => w.Kind == "movie" && w.Genres.Contains("animation")), "live Vampire Hunter D recommendations initially contain animation movies only");
    Console.WriteLine($"Series={series.Id}; seasons={series.Seasons.Count}; season1Episodes={episodes.Count}; firstScore={episodes[0].Rating}; synopsisLength={episodes[0].Overview.Length}; recommended={similar.Items.Count}");
    File.WriteAllText(Path.Combine(".tools", "v084-public-live-data.json"), JsonUtil.Serialize(new { Film = film, Series = series, Episodes = episodes, Collection = collection }));
    Logger.Shutdown(); return;
}
if (args.Contains("--director-live")) {
    var credentialFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TVBox PC", "tmdb.credential");
    if (!File.Exists(credentialFile)) throw new Exception("No configured credential for live director check");
    File.Copy(credentialFile, Path.Combine(root, "tmdb.credential"));
    var person = new TVBoxForWindows.Engine.PersonCatalog(new TVBoxForWindows.Engine.MetadataHttp(new HttpClient()));
    var page = await person.FetchAsync(new CatalogQuery { PersonId = 7467, Category = "movie", PageSize = 100 }, "movie", 1, CancellationToken.None);
    Require(page.Items.Any(w => w.Id == "550"), "director filmography contains directed Fight Club instead of only cast appearances");
    Console.WriteLine($"Live director=7467; movieResults={page.Items.Count}; includesDirected550=true"); Logger.Shutdown(); return;
}
if (args.Contains("--artwork-live")) {
    var credentialFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TVBox PC", "tmdb.credential");
    if (!File.Exists(credentialFile)) throw new Exception("No configured credential for live artwork check");
    File.Copy(credentialFile, Path.Combine(root, "tmdb.credential"));
    var work = await new TVBoxForWindows.Engine.CatalogQueryProvider(new TVBoxForWindows.Engine.MetadataHttp(new HttpClient())).DetailsAsync(new CatalogItem { Provider = "tmdb", Id = "550", Kind = "movie" }, CancellationToken.None);
    Console.WriteLine($"Live poster language={work.PosterLanguage}, artworkSchema={work.ArtworkSchema}, image={work.Poster}; logoLanguage={work.TitleLogoLanguage}; logo={work.TitleLogo}; backdrop={work.Backdrop}; directors={work.Crew.Count}");
    Logger.Shutdown(); return;
}
if (args.Contains("--validate-update-package")) {
    ReleaseUpdate.ExtractCleanPackage(Path.GetFullPath("artifacts/TVBox-x64-0.8.5.zip"), Path.Combine(root, "extracted"));
    Require(File.Exists(Path.Combine(root, "extracted", "libs", "TVBox.dll")), "real public package passes complete updater layout validation");
    Logger.Shutdown(); return;
}
if (args.Contains("--updater-live")) {
    var updates = new ReleaseUpdate(new HttpClient { Timeout = TimeSpan.FromMinutes(15) });
    var release = await updates.CheckAsync("0.8.4", default);
    Require(release != null && release.Version == "0.8.5", "live updater reads the real public stable release");
    var stage = await updates.DownloadAsync(release, Path.GetFullPath(".tools/v086-live-update-download"), null, default);
    Require(File.Exists(Path.Combine(stage.Directory, "TVBox.exe")) && File.Exists(Path.Combine(stage.Directory, "runtime", "host", "fxr", "9.0.22", "hostfxr.dll")), "live updater verifies and stages the complete public portable package");
    File.WriteAllText(".tools/v086-live-update-ready.json", JsonUtil.Serialize(stage));
    Logger.Shutdown(); return;
}
if (args.Contains("--danmaku-shawshank-live")) {
    var result = await new DanmakuSearch().SearchAsync(new CatalogItem { Title = "肖申克的救赎", OriginalTitle = "The Shawshank Redemption", Kind = "movie", Year = "1994" }, "正片", "", CancellationToken.None);
    var engine = new DanmakuEngine(); await engine.LoadAsync(result.Text);
    Console.WriteLine(result.Message + " / parsed=" + engine.Items.Count); Logger.Shutdown(); return;
}
if (args.Contains("--danmaku-live")) {
    var result = await new DanmakuSearch().SearchAsync(new CatalogItem { Title = "武林外传", Kind = "movie", Year = "2011" }, "第1集", "", CancellationToken.None);
    var engine = new DanmakuEngine(); await engine.LoadAsync(result.Text);
    Console.WriteLine(result.Message + " / parsed=" + engine.Items.Count); Logger.Shutdown(); return;
}
if (args.Contains("--danmaku-edgerunners-live")) {
    var work = new CatalogItem { Title = "赛博朋克：边缘行者 第二季", OriginalTitle = "Cyberpunk: Edgerunners Season 2", Kind = "tv", Year = "2026", Aliases = new() { "赛博朋克：边缘行者2" } };
    var result = await new DanmakuSearch().SearchCachedAsync(work, "第1集", "", CancellationToken.None);
    var engine = new DanmakuEngine(); await engine.LoadAsync(result.Text);
    Require(result.LibraryId == "194450001" && engine.Items.Count > 0, "exact second-season first-episode public relay comments");
    Console.WriteLine($"Provider={result.Provider}; libraryId={result.LibraryId}; parsed={engine.Items.Count}; firstMs={engine.Items.First().TimeMs}");
    Logger.Shutdown(); return;
}
foreach (var international in InternationalSources.Sites) SiteRegistry.Set(international.Key, new SitePreference { Enabled = false, Group = "isolated fixture" });
await CatalogRegression.RunAsync(Require);
Require(SubtitlePresentation.Text(null) == "", "subtitle clears between cues");
Require(SubtitlePresentation.Text("{\\an8}<i>中文字幕</i>\\Nsecond &amp; line") == "中文字幕\nsecond & line", "ASS and SRT text presentation");
Require(SubtitlePresentation.Text("1 < 2") == "1 < 2", "subtitle preserves ordinary angle brackets");
Environment.SetEnvironmentVariable("TVBOX_PC_DATA_DIR", root);
AppPaths.Init();
Setting.Load();
Require(AppPaths.Root == root, "isolated profile");
await CatalogV04Regression.RunAsync(Require);
CatalogV05Regression.Run(Require);
CatalogV06Regression.Run(Require);
await CatalogV07Regression.RunAsync(Require);
await CatalogV08Regression.RunAsync(Require);
await CatalogV081Regression.RunAsync(Require);
await CatalogV082Regression.RunAsync(Require);
await CatalogV083Regression.RunAsync(Require);
await CatalogV084Regression.RunAsync(Require);
await CatalogV085Regression.RunAsync(Require);
await CatalogV086Regression.RunAsync(Require);
await CatalogV087Regression.RunAsync(Require);
var bundled = await Decoder.GetJson(BuiltInSources.ConfigUrl);
using (var catalog = JsonDocument.Parse(bundled))
{
    var sites = catalog.RootElement.GetProperty("sites").EnumerateArray().ToList();
    Require(sites.Count == 32, "all 32 original-list sources available without HTTP");
    Require(sites.Select(s => s.GetProperty("key").GetString()).Distinct().Count() == sites.Count, "unique source identities");
    Require(sites.All(s => s.GetProperty("type").GetInt32() == 1 && Uri.TryCreate(s.GetProperty("api").GetString(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"), "first-run sources use HTTP APIs and do not need plugins");
}
Require(AppPaths.ReadAsset("../outside.json") == "", "asset path traversal blocked");
var config = BuiltInSources.ResolveStartup();
Require(config.Url == BuiltInSources.ConfigUrl && config.Name == BuiltInSources.ConfigName, "first run without input");
Stores.SaveConfig(config);
Setting.ConfigVod = config.Url;
Require(BuiltInSources.ResolveStartup().Id == config.Id, "stable default identity");

Stores.SaveKeep(new Keep { Cid = config.Id, Key = "builtin_360@movie", VodName = "测试电影" });
Stores.SaveKeep(new Keep { Cid = config.Id, Key = "builtin_360@movie", VodName = "测试电影" });
Require(Stores.GetKeeps(config.Id).Count == 1, "idempotent favorite");
Stores.SaveHistory(new History { Cid = config.Id, Key = "builtin_360@series", VodName = "测试剧集", EpisodeUrl = "episode-2", Position = 123456, Duration = 2700000 });
Require(Stores.GetHistories(config.Id).Single().EpisodeUrl == "episode-2", "episode identity");
Setting.Incognito = true;
Stores.SaveHistory(new History { Cid = config.Id, Key = "private@movie" });
Require(Stores.FindHistory(config.Id, "private@movie") == null, "incognito does not persist");
Setting.Incognito = false;
Stores.SaveHistory(new History { Cid = config.Id, Key = "douban@123", CatalogId = "123", CatalogKind = "tv", CatalogYear = "2026", SourceKey = "builtin_mdzy", SourceVodId = "provider-id", VodName = "豆瓣剧集", VodRemarks = "第02集", EpisodeUrl = "ep2", Position = 45000, Duration = 2700000 });
Stores.SaveKeep(new Keep { Cid = config.Id, Key = "douban@123", CatalogId = "123", CatalogKind = "tv", CatalogYear = "2026", VodName = "豆瓣剧集" });

using (var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
{
    ArgumentList = { "verify-restart" }, UseShellExecute = false
}))
{
    child.WaitForExit();
    Require(child.ExitCode == 0, "fresh process reload");
}

var custom = Stores.FindConfig("https://example.com/user-config.json", 0);
Stores.SaveConfig(custom);
Setting.ConfigVod = custom.Url;
Require(BuiltInSources.ResolveStartup().Url == custom.Url, "preserve user source");
Setting.ConfigVod = "";
Require(BuiltInSources.ResolveStartup().Url == custom.Url, "recover source when preference lost");

var file = Path.Combine(root, "recovery.json");
Require(DurableJsonFile.Write(file, "{\"value\":1}"), "atomic first write");
Require(DurableJsonFile.Write(file, "{\"value\":2}"), "atomic replacement");
File.WriteAllText(file, "{broken");
var recovered = DurableJsonFile.Read(file, text => JsonSerializer.Deserialize<Dictionary<string, int>>(text), () => null);
Require(recovered?["value"] == 1, "recover damaged JSON from backup");
Logger.Shutdown();
// Only the unique directory created above may be deleted.
if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test path");
Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
Directory.Delete(root, true);
Console.WriteLine("PASS: first-run defaults, stable identity, custom-source preservation, favorites, restart resume, privacy, atomic recovery.");

static void Require(bool condition, string message)
{
    if (!condition) throw new Exception("FAILED: " + message);
    Console.WriteLine("PASS: " + message);
}
