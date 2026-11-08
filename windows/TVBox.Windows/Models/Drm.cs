using System.Text.Json.Serialization;
namespace TVBoxForWindows.Models;
public class Drm
{
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("header")] public Dictionary<string, string> Header { get; set; } = new();
    [JsonPropertyName("forceKey")] public bool ForceKey { get; set; }
}
