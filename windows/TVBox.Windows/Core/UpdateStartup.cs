using System.Diagnostics;
using System.Text.Json;
namespace TVBoxForWindows.Core;
public record CompletedUpdate(string From,string To);
public static class UpdateStartup
{
    public static CompletedUpdate Consume(string profile,string installation,string current)
    {
        var root=Path.Combine(profile,"updates");if(!Directory.Exists(root))return null;
        foreach(var result in Directory.EnumerateFiles(root,"result.json",SearchOption.AllDirectories).OrderByDescending(File.GetLastWriteTimeUtc).Take(50))try {
            var operation=Path.GetDirectoryName(result);var marker=Path.Combine(operation,"startup-notified.json");if(File.Exists(marker))continue;
            using var status=JsonDocument.Parse(File.ReadAllText(result));var s=status.RootElement;
            if(s.GetProperty("State").GetString()!="installed"||s.GetProperty("Version").GetString()!=current)continue;
            using var manifest=JsonDocument.Parse(File.ReadAllText(Path.Combine(operation,"install.json")));var plan=manifest.RootElement;
            if(!Path.GetFullPath(plan.GetProperty("InstallRoot").GetString()).TrimEnd('\\','/').Equals(Path.GetFullPath(installation).TrimEnd('\\','/'),StringComparison.OrdinalIgnoreCase))continue;
            string from=plan.TryGetProperty("FromVersion",out var v)?v.GetString():null;
            if(!Version.TryParse(from,out var previous)) {
                // Older helpers have no restart arguments or FromVersion field.
                // Their backup still contains the original assembly's real version.
                var old=Path.Combine(operation,"backup","libs","TVBox.dll");
                var info=File.Exists(old)?FileVersionInfo.GetVersionInfo(old).FileVersion:null;
                if(!Version.TryParse(info,out previous))continue;
            }
            if(!Version.TryParse(current,out var next)||previous>=next)continue;
            if(!DurableJsonFile.Write(marker,JsonUtil.Serialize(new {From=previous.ToString(3),To=current})))continue;
            return new(previous.ToString(3),current);
        }catch{/* A bad or unrelated receipt never blocks normal startup. */}
        return null;
    }
}
