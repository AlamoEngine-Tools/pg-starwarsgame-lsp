<#
.SYNOPSIS
    Fails when a packaged VSIX lacks what it must ship.

.DESCRIPTION
    A VSIX is a zip whose extension files sit under extension/. Checked: the bundled server, the
    bundled Lua analyzer beside it, and the third-party notices the analyzer's licence requires.

.EXAMPLE
    ./scripts/check-vsix.ps1 dist/aet-eaw-edit-0.5.0.vsix
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Vsix
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$required = @(
    'extension/server/PG.StarWarsGame.LSP.Server.exe',
    'extension/server/emmylua/emmylua_ls.exe',
    'extension/ThirdPartyNotices.txt'
)

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead((Resolve-Path $Vsix))
try {
    $entries = $zip.Entries | ForEach-Object { $_.FullName }
}
finally {
    $zip.Dispose()
}

$missing = $required | Where-Object { $entries -notcontains $_ }
if ($missing) {
    throw "The VSIX lacks: $($missing -join ', ')"
}
Write-Host "VSIX carries the bundled server, analyzer and notices: $Vsix"
