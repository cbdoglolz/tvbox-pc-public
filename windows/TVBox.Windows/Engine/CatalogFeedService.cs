using TVBoxForWindows.Core;
using TVBoxForWindows.Models;
using TVBoxForWindows.Net;

namespace TVBoxForWindows.Engine;

public static class CatalogFeedService
{
    public static async Task<CatalogPage> GetAsync(string kind, int page, bool refresh, CancellationToken ct)
        => await GetAsync("douban", CatalogChannels.For("douban").First(c => c.Key == (kind == "movie" ? "hot-movie" : "hot-tv")), page, refresh, ct);

    public static async Task<CatalogPage> GetAsync(string provider, CatalogChannel channel, int page, bool refresh, CancellationToken ct)
    {
        var kind = channel.Kind;
        page = Math.Max(1, page);
        var path = Path.Combine(AppPaths.Cache, $"catalog-v3-{provider}-{channel.Key}-{page}.json");
        var cached = DurableJsonFile.Read(path, text => JsonUtil.Deserialize<CatalogPage>(text), () => null);
        if (!refresh && cached != null && DateTimeOffset.UtcNow - cached.FetchedAt < TimeSpan.FromMinutes(30)) return cached;
        try
        {
            var headers = new Dictionary<string, string>
            {
                ["User-Agent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/122.0.0.0 Safari/537.36",
                ["Referer"] = $"https://movie.douban.com/{kind}/", ["Origin"] = "https://movie.douban.com",
                ["Accept"] = "application/json, text/plain, */*",
            };
            var query = new Dictionary<string, string>
            {
                ["start"] = ((page - 1) * 20).ToString(), ["limit"] = "20",
                ["category"] = kind == "tv" ? "tv" : "热门", ["type"] = kind == "tv" ? "tv" : "全部",
            };
            var endpoint = $"https://m.douban.com/rexxar/api/v2/subject/recent_hot/{kind}";
            if (channel.Key == "top250")
            {
                endpoint = "https://m.douban.com/rexxar/api/v2/subject_collection/movie_top250/items";
                query = new() { ["start"] = ((page - 1) * 20).ToString(), ["count"] = "20" };
            }
            else if (provider == "netflix" || channel.Key is not ("hot-tv" or "hot-movie"))
            {
                endpoint = $"https://m.douban.com/rexxar/api/v2/{kind}/recommend";
                query = new() { ["start"] = ((page - 1) * 20).ToString(), ["count"] = "20", ["sort"] = channel.Sort, ["tags"] = channel.Tags };
            }
            var response = await HttpUtil.Get(endpoint, headers, query, 12000).WaitAsync(ct);
            if (response.Code is < 200 or >= 300) throw new IOException($"豆瓣推荐暂时不可用（HTTP {response.Code}）");
            var result = CatalogFeedParser.Parse(response.Text(), kind, page);
            ct.ThrowIfCancellationRequested();
            DurableJsonFile.Write(path, JsonUtil.Serialize(result));
            return result;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch when (cached != null && DateTimeOffset.UtcNow - cached.FetchedAt < TimeSpan.FromDays(7))
        {
            cached.IsStale = true;
            return cached;
        }
    }
}
