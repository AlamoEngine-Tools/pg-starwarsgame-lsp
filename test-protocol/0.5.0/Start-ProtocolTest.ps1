<#
.SYNOPSIS
    Starts one test of the 0.5.0 protocol: its files written clean, a fresh VS Code profile with the test's
    settings, the extension under development, and the folder or workspace the test opens.

.DESCRIPTION
    Replaces the hand setup every test used to begin with: the empty profile, the settings the test sets or
    clears, F5, and File > Open Folder. What the test then does and checks is still in the protocol.

    The profile lives in C:\temp\aet-vscode\<TEST-ID> and is created anew on every start, so no setting of
    an earlier test carries over; extensions come from C:\temp\aet-vscode\extensions, which holds none, so no
    other XML extension answers. Every profile sets the local schema and the local baseline folder (each
    project gets its own game's file) unless the test says otherwise - tests.launch.json holds what each
    test opens and sets.

    The test's files are written again first (Setup-TestProtocol.ps1), from clean. -KeepFiles leaves them as
    they are, for a test that continues from the one before it.

.PARAMETER Test
    The test id, e.g. PRJ-24.

.PARAMETER KeepFiles
    Leave the test's files as an earlier test left them.

.PARAMETER Build
    Restage the bundled server and rebuild the client bundle first (Before testing); needs no server running.

.PARAMETER DryRun
    Prepare the files and the profile, print the command line, and start nothing.

.EXAMPLE
    pwsh ./test-protocol/0.5.0/Start-ProtocolTest.ps1 PRJ-24
    pwsh ./test-protocol/0.5.0/Start-ProtocolTest.ps1 SET-01 -Build
#>
param(
    [Parameter(Mandatory, Position = 0)] [string] $Test,
    [switch] $KeepFiles,
    [switch] $Build,
    [switch] $DryRun,
    [string] $EawGameData = 'G:\Games\SteamLibrary\steamapps\common\Star Wars Empire at War\GameData',
    [string] $FocGameData = 'G:\Games\SteamLibrary\steamapps\common\Star Wars Empire at War\corruption'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$client = Join-Path $repo 'PG.StarWarsGame.LSP.Client.VSCode\aet-eaw-edit'
$id = $Test.ToUpperInvariant()
$launch = Get-Content (Join-Path $here 'tests.launch.json') -Raw | ConvertFrom-Json -AsHashtable
if (-not $launch.ContainsKey($id)) { throw "No test $id in tests.launch.json." }
$entry = $launch[$id]

if ($Build) {
    if (Get-Process PG.StarWarsGame.LSP.Server -ErrorAction SilentlyContinue) {
        throw 'A PG.StarWarsGame.LSP.Server is running; close the windows using it before -Build.'
    }
    & pwsh -NoProfile -File (Join-Path $repo 'scripts\stage-bundled-executables.ps1')
    Push-Location $client
    try { npm run bundle } finally { Pop-Location }
}

# LUA-31 runs without the bundled analyzer; every other test with it.
$analyzer = Join-Path $client 'server\emmylua'
if ($id -eq 'LUA-31') {
    if (Test-Path $analyzer) { Move-Item $analyzer "$analyzer.off" -Force }
} elseif (Test-Path "$analyzer.off") {
    if (Test-Path $analyzer) { Remove-Item "$analyzer.off" -Recurse -Force } else { Move-Item "$analyzer.off" $analyzer }
}

# A test on a multi-project mod opens the maintainer's own checkout, which the repository names
# nowhere: local.json beside this script (ignored by git) says where it is.
function Resolve-Mod([string] $value) {
    if ($value -notmatch '\{mod(Leaf)?\}') { return $value }
    $local = Join-Path $here 'local.json'
    if (-not (Test-Path $local)) {
        throw "$id runs on a multi-project mod: write $local as { ""mod"": ""<its folder>"", ""modLeaf"": ""<one leaf project's folder>"" }."
    }
    $paths = Get-Content $local -Raw | ConvertFrom-Json
    return $value.Replace('{modLeaf}', $paths.modLeaf).Replace('{mod}', $paths.mod)
}

$open = if ($entry.open) { Resolve-Mod $entry.open.Replace('{repo}', $repo) } else { $null }

if (-not $KeepFiles) {
    $setup = Join-Path $here 'Setup-TestProtocol.ps1'
    $fixtures = Join-Path $here 'fixtures'
    $own = Test-Path (Join-Path $fixtures $id)
    $extra = @('LUA-25', 'LUA-26', 'LUA-34', 'PRJ-15', 'PRJ-17', 'PRJ-18', 'PRV-36', 'PRJ-24', 'PRJ-25', 'XML-29', 'XML-30') -contains $id
    # A test that writes no files of its own runs in another test's folder: that test writes it clean.
    if (-not $own -and $open -and $open -match '^([CD]):\\temp\\([^\\]+)') {
        $drive, $folder = $Matches[1], $Matches[2]
        $owner = Get-ChildItem $fixtures -Directory | Where-Object {
            Test-Path (Join-Path $_.FullName "$drive\temp\$folder")
        } | Select-Object -First 1
        if ($folder -eq 'aet-test-load-order') { $owner = [pscustomobject]@{ Name = 'PRJ-24' } }
        if ($owner) { & pwsh -NoProfile -File $setup -Test $owner.Name | Out-Null }
    }
    if ($own -or $extra) { & pwsh -NoProfile -File $setup -Test $id | Out-Null }
}

# A fresh profile: the base settings, then the test's own.
$root = 'C:\temp\aet-vscode'
$userData = Join-Path $root $id
if (Test-Path $userData) { Remove-Item $userData -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $userData 'User'), (Join-Path $root 'extensions') | Out-Null

$settings = [ordered]@{
    'security.workspace.trust.enabled' = $false
    'workbench.startupEditor' = 'none'
    'workbench.tips.enabled' = $false
    'update.mode' = 'none'
    'extensions.autoCheckUpdates' = $false
    'telemetry.telemetryLevel' = 'off'
    'aet-eaw-edit.lsp.schema.source' = 'local'
    'aet-eaw-edit.lsp.schema.localPath' = (Join-Path $repo 'schema\eaw')
    'aet-eaw-edit.lsp.source.baseline.type' = 'local'
    'aet-eaw-edit.lsp.source.baseline.localPath' = (Join-Path $repo 'baseline')
}
$testSettings = if ($entry.settings) { $entry.settings } else { @{} }
if ($testSettings.ContainsKey('*')) {
    foreach ($key in @($settings.Keys)) { if ($key -like 'aet-eaw-edit.*') { $settings.Remove($key) } }
}
foreach ($key in $testSettings.Keys) {
    if ($key -eq '*') { continue }
    $value = $testSettings[$key]
    if ($null -eq $value) { $settings.Remove($key); continue }
    if ($value -is [string]) {
        if ($value -eq '{shaders}') {
            # The shader sources are the maintainer's own copy: taken from the normal profile.
            $userSettings = Join-Path $env:APPDATA 'Code\User\settings.json'
            $found = if (Test-Path $userSettings) {
                Select-String -Path $userSettings -Pattern '"aet-eaw-edit\.shaders\.directory"\s*:\s*"([^"]+)"' |
                        Select-Object -First 1
            }
            if (-not $found) { Write-Warning "$id needs aet-eaw-edit.shaders.directory; it is not in $userSettings."; continue }
            $value = $found.Matches[0].Groups[1].Value -replace '\\\\', '\'
        }
        $value = $value.Replace('{repo}', $repo).Replace('{eaw}', $EawGameData).Replace('{foc}', $FocGameData)
    }
    $settings[$key] = $value
}
$settings | ConvertTo-Json | Set-Content (Join-Path $userData 'User\settings.json') -Encoding utf8

$arguments = @('--new-window', '--disable-workspace-trust', "--user-data-dir=$userData",
    "--extensions-dir=$(Join-Path $root 'extensions')")
if ($entry.extension -ne $false) { $arguments += "--extensionDevelopmentPath=$client" }
if ($open) { $arguments += $open }

Write-Host "$id - profile $userData"
Write-Host "  opens: $(if ($open) { $open } else { '(nothing: the test opens it)' })"
foreach ($key in $settings.Keys) {
    if ($key -like 'aet-eaw-edit.*' -or $testSettings.ContainsKey($key)) { Write-Host "  $key = $($settings[$key])" }
}
if ($DryRun) { Write-Host "  code $($arguments -join ' ')" } else { & code @arguments }
