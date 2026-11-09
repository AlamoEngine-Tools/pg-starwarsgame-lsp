<#
.SYNOPSIS
    Creates every file and folder the 0.5.0 test protocol's setups name, so no test needs one made by hand.

.DESCRIPTION
    fixtures\<TEST-ID>\<ROOT>\... holds each test's files, ROOT being C or D (that drive) or REPO (this
    repository, for the scripts the Lua tests add to the vanilla eaw\ folder). Without -Test, every test is
    set up: each C:\temp and D:\temp folder a test uses is deleted and written again, so a test that edited
    its files starts clean. With -Test, only the folders that test uses are reset, and every test sharing
    them is set up again before it.

    On demand only, because they are another state of files other tests use: XML-14 (the XML-01 files with
    stray backticks), XML-15 (Xml_Test.xml as XML-12's first step leaves it), PRJ-15 (beta\beta.pgproj
    renamed to beta.pgproj.off), LUA-26 (no launch.json in eaw\.vscode - an existing one is renamed to
    launch.json.aet-off), LUA-27 and LUA-28 (their launch.json), LUA-34 (stages the bundled executables and
    packs the win32-x64 VSIX to C:\temp\aet-vscode\LUA-34-vsix - minutes, so not part of a full set-up).

    Also done here: PRJ-17 and PRJ-18 copy the archives they read, LUA-25 copies the schema and edits its
    Find_Player annotation, PRV-36 generates its two particle files, and PRJ-24, PRJ-25, XML-29 and
    XML-30 copy the repository's e2e-workspace-load-order to C:\temp\aet-test-load-order, which they edit.

.PARAMETER Test
    One test id, e.g. XML-14. Omit to set up every test.

.PARAMETER EawGameData
    The Empire at War install's GameData folder, for the archives PRJ-18 copies.

.EXAMPLE
    pwsh ./test-protocol/0.5.0/Setup-TestProtocol.ps1
    pwsh ./test-protocol/0.5.0/Setup-TestProtocol.ps1 -Test LUA-27
#>
param(
    [string] $Test,
    [string] $EawGameData = 'G:\Games\SteamLibrary\steamapps\common\Star Wars Empire at War\GameData'
)

$ErrorActionPreference = 'Stop'
$here = $PSScriptRoot
$repo = (Resolve-Path (Join-Path $here '..\..')).Path
$fixtures = Join-Path $here 'fixtures'
$onDemand = @('XML-14', 'XML-15', 'PRJ-15', 'LUA-26', 'LUA-27', 'LUA-28', 'LUA-34')
$loadOrderTests = @('PRJ-24', 'PRJ-25', 'XML-29', 'XML-30')
$extras = @('LUA-25', 'LUA-26', 'LUA-34', 'PRJ-15', 'PRJ-17', 'PRJ-18', 'PRV-36') + $loadOrderTests

function Get-RootOf([string] $root) {
    switch ($root) {
        'REPO' { return $repo }
        default { return "${root}:\" }
    }
}

# Each fixture file and folder as (test, target path).
function Get-Entries([string] $testId) {
    $testDir = Join-Path $fixtures $testId
    if (-not (Test-Path $testDir)) { return @() }
    foreach ($rootDir in Get-ChildItem $testDir -Directory) {
        $target = Get-RootOf $rootDir.Name
        foreach ($item in Get-ChildItem $rootDir.FullName -Recurse -Force) {
            $relative = $item.FullName.Substring($rootDir.FullName.Length + 1)
            [pscustomobject]@{ Test = $testId; Source = $item; Target = Join-Path $target $relative }
        }
    }
}

# The C:\temp or D:\temp folder a target lies in; null for anything else.
function Get-TempRoot([string] $path) {
    if ($path -match '^([CD]:\\temp\\[^\\]+)') { return $Matches[1] }
    return $null
}

function Copy-Entries($entries) {
    foreach ($e in $entries) {
        if ($e.Source.PSIsContainer) {
            New-Item -ItemType Directory -Force $e.Target | Out-Null
        } elseif ($e.Source.Name -ne '.keep') {
            New-Item -ItemType Directory -Force (Split-Path $e.Target) | Out-Null
            Copy-Item $e.Source.FullName $e.Target -Force
        }
    }
}

function Invoke-Extra([string] $testId) {
    if ($loadOrderTests -contains $testId) {
        $dest = 'C:\temp\aet-test-load-order'
        if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
        # Without the index cache the end-to-end runs leave in the repository's copy.
        New-Item -ItemType Directory -Force $dest | Out-Null
        Get-ChildItem (Join-Path $repo 'e2e-workspace-load-order') -Force | Where-Object Name -ne '.aetswg' |
                Copy-Item -Destination $dest -Recurse -Force
        return
    }
    switch ($testId) {
        'PRJ-15' {
            $beta = 'C:\temp\aet-test-PRJ-multi\beta\beta.pgproj'
            if (Test-Path $beta) { Move-Item $beta "$beta.off" -Force }
        }
        'PRJ-17' {
            python (Join-Path $here 'make_archive.py') 'C:\temp\aet-test-PRJ-meg\Data\ScratchScripts.meg'
        }
        'LUA-34' {
            # The shipped package: the bundled server and analyzer staged beside the client, then packed.
            $dest = 'C:\temp\aet-vscode\LUA-34-vsix'
            New-Item -ItemType Directory -Force $dest | Out-Null
            & (Join-Path $repo 'scripts\stage-bundled-executables.ps1')
            Push-Location (Join-Path $repo 'PG.StarWarsGame.LSP.Client.VSCode\aet-eaw-edit')
            try {
                npx --yes @vscode/vsce package --no-dependencies --target win32-x64 -o (Join-Path $dest 'aet-eaw-edit.vsix')
                if ($LASTEXITCODE -ne 0) { throw "LUA-34: packaging the VSIX failed." }
            }
            finally { Pop-Location }
        }
        'PRJ-18' {
            # Named as MegaFiles.xml lists them.
            foreach ($pair in @(@('models.meg', 'Models.meg'), @('textures.meg', 'Textures.meg'))) {
                $source = Join-Path $EawGameData "Data\$($pair[0])"
                $dest = Join-Path 'C:\temp\aet-test-PRJ-preview\Data' $pair[1]
                if (Test-Path $source) { Copy-Item $source $dest -Force }
                else { Write-Warning "PRJ-18: $source not found; $dest is missing." }
            }
        }
        'LUA-25' {
            $dest = 'C:\temp\aet-test-lua25\schema'
            if (Test-Path $dest) { Remove-Item $dest -Recurse -Force }
            New-Item -ItemType Directory -Force $dest | Out-Null
            Get-ChildItem (Join-Path $repo 'schema') -Force | Where-Object Name -ne '.git' |
                    Copy-Item -Destination $dest -Recurse -Force
            $api = Join-Path $dest 'lua\api.d.lua'
            $lines = [System.IO.File]::ReadAllLines($api)
            # The stub is generated and its line numbers move with every regeneration, so the
            # annotation is found by content: the Faction reference tag right above Find_Player.
            $function = [Array]::FindIndex($lines, [Predicate[string]] { param($l) $l -like 'function Find_Player(*' })
            $tag = -1
            if ($function -gt 0) {
                for ($i = $function - 1; $i -ge 0 -and $lines[$i] -like '---*'; $i--) {
                    if ($lines[$i] -eq '---@aetref XmlObject:Faction') { $tag = $i; break }
                }
            }
            if ($tag -lt 0) {
                Write-Warning "LUA-25: no '---@aetref XmlObject:Faction' above Find_Player in $api; left as it is."
            } else {
                $lines[$tag] = '---@xmlref XmlObject:Faction'
                [System.IO.File]::WriteAllLines($api, $lines)
            }
        }
        'LUA-26' {
            $launch = Join-Path $repo 'eaw\.vscode\launch.json'
            if (Test-Path $launch) { Move-Item $launch "$launch.aet-off" -Force }
        }
        'PRV-36' {
            $source = Join-Path $repo 'eaw\Data\Art\Models\P_fire_small00.alo'
            if (Test-Path $source) {
                python (Join-Path $here 'make_particles.py') $source 'C:\temp\aet-prv-pe\Data\Art\Models\P_prv'
            } else { Write-Warning "PRV-36: $source not found; the particle files are missing." }
        }
        'PRV-38' {
            # The mod's mega texture is a Windows bitmap under the .tga name, as two shipped mods keep theirs.
            python (Join-Path $here 'make_icons.py') 'C:\temp\aet-test-PRV-bmp\Data\Art\Textures\mt_commandbar'
        }
        'PRV-35' {
            # The game loads an object file only when its registry lists it, and of two definitions the
            # first read counts: the mod's list names its file, then the base's, then the game's own.
            # Written from the local game workspace, so no game file is part of the fixture.
            $source = Join-Path $repo 'foc\Data\Xml\Gameobjectfiles.xml'
            $dest = 'C:\temp\aet-prv-ovr\mod\Data\XML\GameObjectFiles.xml'
            if (Test-Path $source) {
                $list = [System.IO.File]::ReadAllText($source) -replace '<Game_Object_Files>',
                    "<Game_Object_Files>`r`n`t<File>PrvModUnits.xml</File>`r`n`t<File>PrvBaseUnits.xml</File>"
                [System.IO.File]::WriteAllText($dest, $list)
            } else { Write-Warning "PRV-35: $source not found; $dest is missing and no object file loads." }
        }
    }
}

$allTests = Get-ChildItem $fixtures -Directory | ForEach-Object Name
$allTests = @($allTests) + ($extras | Where-Object { $allTests -notcontains $_ })
$selected = if ($Test) { @($Test.ToUpperInvariant()) } else { $allTests | Where-Object { $onDemand -notcontains $_ } }

# Reset the temp folders the selected tests use, then set up every test that shares one, the selected last.
$roots = $selected | ForEach-Object { Get-Entries $_ } | ForEach-Object { Get-TempRoot $_.Target } |
        Where-Object { $_ } | Sort-Object -Unique
$sharing = $allTests | Where-Object { $onDemand -notcontains $_ -and $selected -notcontains $_ } | Where-Object {
    $t = $_; Get-Entries $t | Where-Object { $roots -contains (Get-TempRoot $_.Target) }
}
# A window or a server still holding a file open in a folder makes the delete fail part-way: the files
# are then written over what is left, and only what the test does not write survives.
foreach ($root in $roots) {
    if (-not (Test-Path $root)) { continue }
    try { Remove-Item $root -Recurse -Force -ErrorAction Stop }
    catch { Write-Warning "Could not remove all of $root (a file is in use); writing over it: $($_.Exception.Message)" }
}

foreach ($t in @($sharing) + @($selected)) {
    Copy-Entries (Get-Entries $t)
    Invoke-Extra $t
    Write-Host "set up $t"
}
