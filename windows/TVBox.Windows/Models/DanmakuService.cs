using System.Security.Cryptography;
using System.Text;
using TVBoxForWindows.Core;

namespace TVBoxForWindows.Models;

public record DanmakuService(string Id, string Name, string Url, bool Enabled = true, string AppId = "", string AppSecret = "")
{
    public bool Official => Url.TrimEnd('/') == "https://api.dandanplay.net/api/v2";
    public static List<DanmakuService> All() => ServiceCredentials.Read("danmaku", () => new List<DanmakuService>());
    public static void Save(List<DanmakuService> services) => ServiceCredentials.Save("danmaku", services);
    public static string Signature(string appId, long timestamp, string path, string secret) => Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(appId + timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture) + path + secret)));
}
