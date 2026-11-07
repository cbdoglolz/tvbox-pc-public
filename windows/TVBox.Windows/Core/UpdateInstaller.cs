using System.Diagnostics;
using System.Security.Cryptography;

namespace TVBoxForWindows.Core;

public static class UpdateInstaller
{
    public static string Prepare(StagedUpdate staged, string installRoot, string scriptPath)
    {
        var root = Path.GetFullPath(installRoot).TrimEnd('\\', '/');
        if (root == Path.GetPathRoot(root)?.TrimEnd('\\', '/') || !File.Exists(Path.Combine(root, "TVBox.exe"))
            || !Directory.Exists(Path.Combine(root, "runtime", "host", "fxr")))
            throw new IOException("当前不是完整便携安装目录，请从发布页面下载完整包");
        var stage = Path.GetFullPath(staged.Directory);
        if (stage.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("更新暂存区必须位于程序目录之外");
        var operation = Path.GetDirectoryName(stage);
        var helper = Path.Combine(operation, "install-update.ps1"); File.Copy(scriptPath, helper, true);
        var files = Directory.EnumerateFiles(stage, "*", SearchOption.AllDirectories).Select(path =>
        {
            var relative = Path.GetRelativePath(stage, path).Replace('\\', '/');
            if (!ReleaseUpdate.AllowedPackagePath(relative)) throw new IOException("更新文件不在程序白名单中");
            using var input = File.OpenRead(path);
            return new { Path = relative, Sha256 = Convert.ToHexString(SHA256.HashData(input)) };
        }).ToList();
        if (files.Count == 0) throw new IOException("更新暂存区为空");
        using var process = Process.GetCurrentProcess();
        var manifest = Path.Combine(operation, "install.json");
        if (!DurableJsonFile.Write(manifest, JsonUtil.Serialize(new
        {
            staged.Version, InstallRoot = root, StageRoot = stage, BackupRoot = Path.Combine(operation, "backup"),
            ProcessId = process.Id, ProcessStartTicks = process.StartTime.ToUniversalTime().Ticks, Files = files,
        }))) throw new IOException("无法保存更新安装计划");
        return manifest;
    }

    public static void Launch(string manifest)
    {
        var helper = Path.Combine(Path.GetDirectoryName(manifest), "install-update.ps1");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(helper) };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper, "-Manifest", manifest }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("无法启动更新安装程序");
    }
}
