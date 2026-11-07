[CmdletBinding()]
param([switch]$Portable)
$ErrorActionPreference = 'Stop'
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$sdk = Join-Path $repo '.tools\dotnet\dotnet.exe'
if (-not (Test-Path -LiteralPath $sdk)) {
    $sdk = (Get-Command dotnet -ErrorAction Stop).Source
}
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ROOT = Split-Path $sdk
$env:PATH = (Split-Path $sdk) + [IO.Path]::PathSeparator + $env:PATH
$project = Join-Path $repo 'windows\TVBox.Windows\TVBox.Windows.csproj'
Push-Location $repo
try {
    & $sdk run --project tests\TVBox.PC.Tests\TVBox.PC.Tests.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Persistence regression checks failed.' }
    & $sdk restore $project -r win-x64
    if ($LASTEXITCODE -ne 0) { throw 'Dependency restore failed.' }
    & $sdk build $project -c Release -p:Platform=x64 -r win-x64 --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'Strict build failed.' }
    if ($Portable) {
        $version = ([xml](Get-Content -LiteralPath $project -Raw)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
        & (Join-Path $PSScriptRoot 'Publish-Portable.ps1') -Version $version
    }
} finally { Pop-Location }
