using System.Text.RegularExpressions;
namespace TVBoxForWindows.Models;
public record SportsLiveArgs(List<Live> Sources);
public static class SportsChannels
{
    static readonly Regex Sports=new(@"体育|足球|篮球|网球|台球|乒乓|羽毛|赛车|高尔夫|看球|赛事|CCTV\s*[-－]?\s*(?:5\+?|16)(?!\d)|\b(?:ESPN\d*|SPORTS?\d*|BEIN|EUROSPORT\d*|DAZN|TSN\d*|NBA|NFL|F1)\b",RegexOptions.IgnoreCase|RegexOptions.CultureInvariant);
    public static bool Matches(string name,string group="")=>Sports.IsMatch((name??"")+" "+(group??""));
    public static bool IsBroadcast(string name)=>Regex.IsMatch(name??"",@"CCTV\s*[-－]?\s*(?:5\+?|16)(?!\d)|\b(?:ESPN\d*|BEIN|EUROSPORT\d*|DAZN|TSN\d*)\b",RegexOptions.IgnoreCase);
    public static void Filter(Live live)
    {
        foreach(var group in live.Groups)group.Channel=group.Channel.Where(c=>Matches(c.Name,group.Name)).ToList();
        live.Groups=live.Groups.Where(g=>g.Channel.Count>0).ToList();
        foreach(var group in live.Groups)foreach(var channel in group.Channel){channel.Group=group;channel.Live=live;}
    }
}
