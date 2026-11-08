using System.Net.Http;
using System.Text;
using System.Text.Json;
using TVBoxForWindows.Core;
using TVBoxForWindows.Models;
namespace TVBoxForWindows.Engine;

/// <summary>Season-year catalogue; ratings retain their real provider identity.</summary>
public sealed class AnimeCatalog(HttpClient client=null,MetadataStore store=null)
{
    static readonly SemaphoreSlim Gate=new(2);
    MetadataStore Store=>store??MetadataStore.Instance;
    const string Query="query($page:Int,$from:FuzzyDateInt,$to:FuzzyDateInt,$search:String){Page(page:$page,perPage:50){pageInfo{hasNextPage}media(type:ANIME,startDate_greater:$from,startDate_lesser:$to,search:$search,isAdult:false,sort:POPULARITY_DESC){id title{native romaji english}synonyms format startDate{year month day}episodes averageScore coverImage{large}bannerImage genres countryOfOrigin popularity}}}";
    async Task<string> Post(string url,object body,CancellationToken ct)
    {
        await Gate.WaitAsync(ct);
        try {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(ct);timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var req=new HttpRequestMessage(HttpMethod.Post,url){Content=new StringContent(JsonSerializer.Serialize(body),Encoding.UTF8,"application/json")};
            req.Headers.UserAgent.ParseAdd("LumaDesktop/0.8.9");
            using var response=await (client??Net.HttpUtil.Client()).SendAsync(req,HttpCompletionOption.ResponseHeadersRead,timeout.Token);
            response.EnsureSuccessStatusCode();await using var stream=await response.Content.ReadAsStreamAsync(timeout.Token);using var memory=new MemoryStream();var buffer=new byte[16384];int n;
            while((n=await stream.ReadAsync(buffer,timeout.Token))>0){if(memory.Length+n>4*1024*1024)throw new IOException("新番资料过大");memory.Write(buffer,0,n);}
            return Encoding.UTF8.GetString(memory.ToArray());
        }finally{Gate.Release();}
    }
    public async Task<CatalogProviderPage> Fetch(CatalogQuery q,int page,CancellationToken ct)
    {
        var first=q.YearFrom??1800;var last=q.YearTo??DateTime.Now.Year;var key=q.CacheKey(page);
        var cached=Store.Read<CatalogProviderPage>("anime-season",key);if(cached?.IsFresh==true)return cached.Value;
        try {
            var json=await Post("https://graphql.anilist.co",new{query=Query,variables=new{page,from=first*10000,to=(last+1)*10000,search=string.IsNullOrWhiteSpace(q.Keyword)?null:q.Keyword.Trim()}},ct);
            var result=Parse(json);
            // Bangumi supplements Chinese names by exact original-title + year, never fuzzy substitution.
            try {
                var bgkey=$"{first}:{last}:{page}";var bg=Store.Read<string>("anime-chinese",bgkey);
                var chinese=bg?.IsFresh==true?bg.Value:await Post($"https://api.bgm.tv/v0/search/subjects?limit=20&offset={(page-1)*20}",new{keyword="",sort="heat",filter=new{type=new[]{2},air_date=new[]{$">={first}-01-01",$"<{last+1}-01-01"}}},ct);
                if(bg?.IsFresh!=true)Store.Write("anime-chinese",bgkey,chinese,TimeSpan.FromHours(12));
                Supplement(result.Items,chinese);
            }catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}catch{}
            Store.Write("anime-season",key,result,TimeSpan.FromHours(12));return result;
        }catch(OperationCanceledException)when(ct.IsCancellationRequested){throw;}
        catch when(cached!=null){return cached.Value with{IsStale=true,ScopeNote="新番目录暂不可用，保留已缓存内容"};}
        catch{throw new MetadataServiceException("新番资料暂不可用，请稍后刷新；已有点播不受影响");}
    }
    public static CatalogProviderPage Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement.GetProperty("data").GetProperty("Page");var list=new List<CatalogItem>();
        string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind is JsonValueKind.String or JsonValueKind.Number?v.ToString():"";
        foreach(var m in root.GetProperty("media").EnumerateArray()) {
            var titles=m.GetProperty("title");var native=Text(titles,"native");var title=native.Length>0?native:Text(titles,"romaji");if(title.Length==0)continue;
            var date=m.GetProperty("startDate");var year=Text(date,"year");var form=Text(m,"format");
            var w=new CatalogItem{Provider="anilist",Id=Text(m,"id"),Title=title,OriginalTitle=title,Year=year,Kind=form=="MOVIE"?"movie":"tv",AnimeForm=form=="MOVIE"?"movie":form=="OVA"?"ova":"series",Poster=Text(m.GetProperty("coverImage"),"large"),Backdrop=Text(m,"bannerImage"),Genres=new(){"animation"},Countries=new(){Text(m,"countryOfOrigin")},Popularity=m.GetProperty("popularity").GetDouble(),EpisodeCount=int.TryParse(Text(m,"episodes"),out var episodes)?episodes:null};
            w.Aliases=new[]{Text(titles,"native"),Text(titles,"romaji"),Text(titles,"english")}.Concat(m.GetProperty("synonyms").EnumerateArray().Select(v=>v.GetString())).Where(s=>!string.IsNullOrWhiteSpace(s)).Distinct().ToList();
            if(int.TryParse(year,out var y)&&int.TryParse(Text(date,"month"),out var month))w.ReleaseDate=$"{y:D4}-{month:D2}-{(int.TryParse(Text(date,"day"),out var day)?day:1):D2}";
            if(m.TryGetProperty("averageScore",out var score)&&score.ValueKind==JsonValueKind.Number&&score.TryGetDouble(out var rating))w.Ratings.Add(new("anilist",rating/10,null,DateTimeOffset.UtcNow,"AniList"));
            w.RawGenres=m.GetProperty("genres").EnumerateArray().Select(g=>g.GetString()).Where(g=>g!=null).ToList();
            w.Genres=w.Genres.Concat(CatalogVocabulary.GenreKeys(w.RawGenres)).Distinct().ToList();
            w.Subtitle=year+" · "+form;list.Add(w);
        }
        return new(list,root.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean(),null,"AniList年度新番 · Bangumi补充中文标题；年份按首播日期，目录收录不代表存在播放源");
    }
    public static void Supplement(List<CatalogItem> works,string json)
    {
        using var doc=JsonDocument.Parse(json);
        foreach(var work in works) {
            var matches=doc.RootElement.GetProperty("data").EnumerateArray().Where(b=>b.TryGetProperty("date",out var date)&&date.GetString()?.StartsWith(work.Year+"-")==true&&b.TryGetProperty("name",out var name)&&work.Aliases.Any(a=>TitleMatchRules.Normalize(a)==TitleMatchRules.Normalize(name.GetString()))&&b.TryGetProperty("name_cn",out var cn)&&!string.IsNullOrWhiteSpace(cn.GetString())).ToList();
            if(matches.Count!=1)continue;var b=matches[0];work.Title=b.GetProperty("name_cn").GetString();
            if(b.TryGetProperty("summary",out var synopsis))work.Overview=synopsis.GetString()??"";
        }
    }
}
