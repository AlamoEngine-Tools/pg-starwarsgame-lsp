<#
.SYNOPSIS
    Puts the two executables the VSIX ships into the extension's server folder.

.DESCRIPTION
    The extension is packaged for win32-x64 and carries its own executables:
      server/PG.StarWarsGame.LSP.Server.exe   the language server, self-contained, single file
      server/emmylua/emmylua_ls.exe           the Lua analyzer, where the server looks for it
    This script publishes the first and downloads the second at the pinned version, refusing
    an archive whose SHA-256 differs from the pinned one. The folder is emptied first, so a
    stale file never ships. Used by release.ps1 and by both CI workflows.

.EXAMPLE
    ./scripts/stage-bundled-executables.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# The analyzer the extension ships. A new version changes all three, together with the version
# and copyright lines in the extension's ThirdPartyNotices.txt.
$EmmyLuaVersion = '0.25.1'
$EmmyLuaUrl     = "https://github.com/EmmyLuaLs/emmylua-analyzer-rust/releases/download/$EmmyLuaVersion/emmylua_ls-win32-x64.zip"
$EmmyLuaSha256  = '82efa133287f67be09e1a624d8efecd8063832ec2b9bf9279c37afc5f42118bd'

$root       = Split-Path $PSScriptRoot -Parent
$serverProj = Join-Path $root 'PG.StarWarsGame.LSP.Server/PG.StarWarsGame.LSP.Server.csproj'
$serverDir  = Join-Path $root 'PG.StarWarsGame.LSP.Client.VSCode/aet-eaw-edit/server'
$analyzerDir = Join-Path $serverDir 'emmylua'

if (Test-Path $serverDir) { Remove-Item $serverDir -Recurse -Force }
New-Item $analyzerDir -ItemType Directory -Force | Out-Null

Write-Host "==> Publishing the server ($Configuration, win-x64, self-contained)"
dotnet publish $serverProj `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:DebugType=None `
    --output $serverDir
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed' }

Write-Host "==> Downloading emmylua_ls $EmmyLuaVersion"
$archive = Join-Path ([IO.Path]::GetTempPath()) "emmylua_ls-$EmmyLuaVersion-$([guid]::NewGuid().ToString('N')).zip"
try {
    Invoke-WebRequest -Uri $EmmyLuaUrl -OutFile $archive -UseBasicParsing
    $actual = (Get-FileHash $archive -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $EmmyLuaSha256) {
        throw "emmylua_ls archive SHA-256 is $actual, expected $EmmyLuaSha256 - refusing to ship it"
    }
    Expand-Archive -Path $archive -DestinationPath $analyzerDir -Force
}
finally {
    if (Test-Path $archive) { Remove-Item $archive -Force }
}

foreach ($file in (Join-Path $serverDir 'PG.StarWarsGame.LSP.Server.exe'), (Join-Path $analyzerDir 'emmylua_ls.exe')) {
    if (-not (Test-Path $file)) { throw "Missing after staging: $file" }
}
Write-Host "    Staged into $serverDir"
