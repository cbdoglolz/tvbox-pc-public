param([Parameter(Mandatory=$true)][string]$Manifest, [switch]$NoRestart)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$resultFile = Join-Path (Split-Path -Parent $Manifest) 'result.json'
$journal = [System.Collections.Generic.List[object]]::new()
$plan = $null
function FileHash([string]$Path) {
    $stream = [IO.File]::OpenRead($Path)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '') }
    finally { $sha.Dispose(); $stream.Dispose() }
}
function ChildPath([string]$Parent, [string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative.Contains(':') -or ($Relative -split '[/\\]' | Where-Object { $_ -in @('', '.', '..') })) { throw 'Unsafe relative update path' }
    $base = [IO.Path]::GetFullPath($Parent).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath((Join-Path $base $Relative))
    if (-not $target.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Update path outside destination' }
    $cursor = $target
    while ($cursor -and $cursor.Length -ge $base.TrimEnd('\','/').Length) {
        if (Test-Path -LiteralPath $cursor) {
            if (((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Update destination contains a reparse point' }
        }
        $cursor = Split-Path -Parent $cursor
    }
    return $target
}
function WriteResult([string]$State, [string]$Message) {
    @{ State=$State; Message=$Message; Version=if ($plan) { $plan.Version } else { '' }; Date=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json | Set-Content -LiteralPath $resultFile -Encoding UTF8
}
try {
    $plan = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
    $targetRoot = [IO.Path]::GetFullPath($plan.InstallRoot).TrimEnd('\','/')
    $stageRoot = [IO.Path]::GetFullPath($plan.StageRoot).TrimEnd('\','/')
    $backupRoot = [IO.Path]::GetFullPath($plan.BackupRoot).TrimEnd('\','/')
    $operation = [IO.Path]::GetFullPath((Split-Path -Parent $Manifest)).TrimEnd('\','/')
    if ($targetRoot -eq [IO.Path]::GetPathRoot($targetRoot).TrimEnd('\','/') -or -not (Test-Path -LiteralPath (Join-Path $targetRoot 'TVBox.exe'))) { throw 'Invalid portable installation directory' }
    if ($stageRoot -ne (Join-Path $operation 'files') -or $backupRoot -ne (Join-Path $operation 'backup') -or $operation.StartsWith($targetRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid update staging directory' }
    $appProcess = Get-Process -Id $plan.ProcessId -ErrorAction SilentlyContinue
    if ($appProcess -and $appProcess.StartTime.ToUniversalTime().Ticks -eq [long]$plan.ProcessStartTicks) {
        if (-not $appProcess.WaitForExit(180000)) { throw 'Application has not exited; update cancelled without terminating it' }
    }
    # Validate every file before touching the existing install. Never import profile directories.
    foreach ($file in $plan.Files) {
        $source = ChildPath $stageRoot $file.Path
        $null = ChildPath $targetRoot $file.Path
        $null = ChildPath $backupRoot $file.Path
        if ((FileHash $source) -ne $file.Sha256) { throw 'Staged update hash mismatch' }
        $parts = $file.Path -split '/'
        if ($parts[0] -notin @('assets','libs','ffmpeg','node','runtime','locales','docs','TVBox.exe','README.md','LICENSE','THIRD-PARTY-NOTICES.md') -or
            [IO.Path]::GetFileName($file.Path) -in @('prefs.json','configs.json','history.json','keep.json','source-statistics.json','app.log','tokens.json','cookies.json','credentials.json') -or $file.Path -match '\.credential(?:$|\.)') { throw 'Personal data or unapproved path in update' }
    }
    foreach ($file in $plan.Files) {
        $source = ChildPath $stageRoot $file.Path
        $target = ChildPath $targetRoot $file.Path
        $backup = ChildPath $backupRoot $file.Path
        $existed = Test-Path -LiteralPath $target
        if ($existed) {
            $null = New-Item -ItemType Directory -Path (Split-Path -Parent $backup) -Force
            Copy-Item -LiteralPath $target -Destination $backup
        }
        $null = New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force
        $journal.Add(@{ Target=$target; Backup=$backup; Existed=$existed })
        Copy-Item -LiteralPath $source -Destination $target -Force
        if ((FileHash $target) -ne $file.Sha256) { throw 'Installed update hash mismatch' }
    }
    WriteResult 'installed' 'Update installed; previous files retained in the update backup'
    if (-not $NoRestart) { Start-Process -FilePath (Join-Path $targetRoot 'TVBox.exe') -WorkingDirectory $targetRoot -WindowStyle Normal }
    exit 0
} catch {
    $restored = $true
    for ($i=$journal.Count-1; $i -ge 0; $i--) {
        $row = $journal[$i]
        try {
            if ($row.Existed) { Copy-Item -LiteralPath $row.Backup -Destination $row.Target -Force }
            elseif (Test-Path -LiteralPath $row.Target) { Remove-Item -LiteralPath $row.Target -Force }
        } catch { $restored = $false }
    }
    WriteResult $(if ($restored) { 'cancelled-or-restored' } else { 'restore-needs-attention' }) $(if ($restored) { 'Update failed; existing application files preserved or restored' } else { 'Update failed; close other application instances and restore from the update backup' })
    exit 1
}
