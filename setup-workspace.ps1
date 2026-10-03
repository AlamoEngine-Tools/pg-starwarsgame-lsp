#Requires -Version 7.0
<#
.SYNOPSIS
    Sets up the repositories the LSP builds against, as listed in workspace.deps.

.DESCRIPTION
    sync (default)  Clone what is missing, fast-forward owned repos on their default branch, move
                    external repos to their pinned commit. Never touches a repo with local work.
    check           Report the state of every repo without changing anything but remote refs.
    record          Write the current commit of every external repo into workspace.deps.

    Exit codes: 0 done, 1 a repo failed, 2 bad arguments or manifest.

    setup-workspace.sh is the bash twin of this script. Both print the same status lines;
    scripts/workspace-setup.test.js runs them side by side and fails when they differ.

.PARAMETER Groups
    Groups to act on: build, e2e. Default: build

.PARAMETER MatchBranch
    Owned repos with a remote branch of this name use it instead of their default branch. CI
    passes the pull request's branch.

.PARAMETER Manifest
    Read another manifest. Paths in it stay relative to this repo.

.PARAMETER Summary
    Append the status lines to this file (GitHub step summary).

.PARAMETER NoSelfUpdate
    Do not fast-forward this repo before syncing.

.EXAMPLE
    ./setup-workspace.ps1

.EXAMPLE
    ./setup-workspace.ps1 check -Groups build,e2e
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0)]
    [string] $Command = 'sync',
    [string[]] $Groups = @('build'),
    [string] $MatchBranch = '',
    [string] $Manifest = '',
    [string] $Summary = '',
    [switch] $NoSelfUpdate
)

$PSNativeCommandUseErrorActionPreference = $false
$Root = $PSScriptRoot
$script:Failed = $false
$script:Out = [System.Collections.Generic.List[string]]::new()

# ── Output ───────────────────────────────────────────────────────────────────

function Write-Status([string] $Level, [string] $Name, [string] $Message) {
    $line = "[$Level] $Name - $Message"
    [Console]::Out.WriteLine($line)
    $script:Out.Add($line)
    if ($Level -eq 'fail') { $script:Failed = $true }
}

function Stop-Usage([string] $Message) {
    [Console]::Error.WriteLine($Message)
    exit 2
}

function Stop-Manifest([int] $Line, [string] $Message) {
    [Console]::Error.WriteLine("$(Split-Path -Leaf $Manifest):$Line - $Message")
    exit 2
}

# ── Arguments ────────────────────────────────────────────────────────────────

if ($Command -cnotin @('sync', 'check', 'record')) { Stop-Usage "Unknown argument: $Command" }

# -Groups build,e2e arrives as an array from PowerShell and as one string through -File.
$Selected = @($Groups | ForEach-Object { $_ -split ',' } | Where-Object { $_ -ne '' })
foreach ($g in $Selected) {
    if ($g -cnotin @('build', 'e2e')) { Stop-Usage "Unknown group: $g" }
}
if ($Selected.Count -eq 0) { Stop-Usage '-Groups needs at least one group' }

if (-not $Manifest) { $Manifest = Join-Path $Root 'workspace.deps' }

# ── Manifest ─────────────────────────────────────────────────────────────────

# A path stays inside the workspace: relative, forward slashes, at most one leading "..".
function Test-WorkspacePath([string] $Path) {
    if ($Path -match '^[/\\]' -or $Path -match '^[A-Za-z]:' -or $Path.Contains('\')) { return $false }
    $segments = [System.Collections.Generic.List[string]]::new($Path.Split('/'))
    if ($segments.Count -gt 1 -and $segments[$segments.Count - 1] -eq '') { $segments.RemoveAt($segments.Count - 1) }
    for ($i = 0; $i -lt $segments.Count; $i++) {
        $seg = $segments[$i]
        if ($seg -eq '' -or $seg -eq '.') { return $false }
        if ($seg -eq '..' -and -not ($i -eq 0 -and $segments.Count -ge 2)) { return $false }
    }
    return $true
}

if (-not (Test-Path -LiteralPath $Manifest -PathType Leaf)) { Stop-Usage "Manifest not found: $Manifest" }

$Rows = [System.Collections.Generic.List[object]]::new()
$lineNo = 0
foreach ($raw in [IO.File]::ReadAllLines($Manifest)) {
    $lineNo++
    $line = $raw
    $hash = $line.IndexOf('#')
    if ($hash -ge 0) { $line = $line.Substring(0, $hash) }
    $cols = @($line -split '\s+' | Where-Object { $_ -ne '' })
    if ($cols.Count -eq 0) { continue }
    if ($cols.Count -ne 5) { Stop-Manifest $lineNo "Expected 5 columns, found $($cols.Count)" }
    $name, $path, $group, $ref, $url = $cols

    if ($name -cnotmatch '^[A-Za-z0-9._-]+$') { Stop-Manifest $lineNo "Invalid name: $name" }
    if ($Rows | Where-Object { $_.Name -ceq $name }) { Stop-Manifest $lineNo "Duplicate name: $name" }
    if (-not (Test-WorkspacePath $path)) { Stop-Manifest $lineNo "Path must stay inside the workspace: $path" }
    if ($group -cnotin @('build', 'e2e')) { Stop-Manifest $lineNo "Unknown group: $group" }
    if ($ref -cne 'default' -and $ref -cnotmatch '^[0-9a-f]{40}$') {
        Stop-Manifest $lineNo "Ref must be `"default`" or a 40-character commit SHA: $ref"
    }
    if ($url -cnotmatch '^(https?|file|ssh)://\S+$' -and $url -cnotmatch '^git@\S+$') {
        Stop-Manifest $lineNo "Unsupported URL: $url"
    }
    $Rows.Add([pscustomobject]@{ Name = $name; Path = $path; Group = $group; Ref = $ref; Url = $url })
}

# ── Git helpers ──────────────────────────────────────────────────────────────

# Runs git with stderr discarded and returns stdout as one trimmed string; $LASTEXITCODE survives.
function Get-Git {
    $out = & git @args 2>$null
    if ($null -eq $out) { return '' }
    return ((@($out) | ForEach-Object { "$_" }) -join "`n").Trim()
}

function Test-Git {
    & git @args 2>$null | Out-Null
    return $LASTEXITCODE -eq 0
}

function Get-Short([string] $Sha) { if ($Sha.Length -gt 7) { $Sha.Substring(0, 7) } else { $Sha } }

function Get-NormalizedUrl([string] $Url) {
    return ($Url.ToLowerInvariant() -replace '/+$', '') -replace '\.git$', ''
}

# The remote of an existing clone whose URL is the manifest's, so a fork as origin still works.
function Find-Remote([string] $Dir, [string] $Url) {
    $want = Get-NormalizedUrl $Url
    foreach ($r in ((Get-Git -C $Dir remote) -split "`n")) {
        if (-not $r) { continue }
        if ((Get-NormalizedUrl (Get-Git -C $Dir remote get-url $r)) -eq $want) { return $r }
    }
    return $null
}

function Get-DefaultBranch([string] $Where, [string] $Dir = $null) {
    $out = if ($Dir) { Get-Git -C $Dir ls-remote --symref $Where HEAD } else { Get-Git ls-remote --symref $Where HEAD }
    foreach ($l in ($out -split "`n")) {
        if ($l -match '^ref:\s+refs/heads/(\S+)\s+HEAD$') { return $Matches[1] }
    }
    return ''
}

function Test-RemoteBranch([string] $Url, [string] $Branch) {
    return [bool](Get-Git ls-remote --heads $Url "refs/heads/$Branch")
}

function Get-CurrentBranch([string] $Dir) { return Get-Git -C $Dir symbolic-ref --short -q HEAD }

function Get-Head([string] $Dir) { return Get-Git -C $Dir rev-parse HEAD }

# Untracked files never block a fast-forward that does not touch them, so they do not count.
function Test-Dirty([string] $Dir) { return [bool](Get-Git -C $Dir status --porcelain --untracked-files=no) }

function Test-LocalOnly([string] $Dir) { return [bool](Get-Git -C $Dir rev-list -n 1 HEAD --not --remotes) }

function Test-Commit([string] $Dir, [string] $Sha) { return Test-Git -C $Dir cat-file -e "$Sha^{commit}" }

function Test-OnRemote([string] $Dir, [string] $Remote, [string] $Sha) {
    return [bool](Get-Git -C $Dir for-each-ref --contains $Sha "refs/remotes/$Remote/")
}

# Makes sure a pinned commit is present, fetching it by SHA if no branch carried it.
function Confirm-Commit([string] $Dir, [string] $Remote, [string] $Sha) {
    if (Test-Commit $Dir $Sha) { return $true }
    Test-Git -C $Dir fetch --quiet $Remote $Sha | Out-Null
    return (Test-Commit $Dir $Sha)
}

# Common opening for an existing repo: is it one, which remote, fetch. Returns the remote or $null.
function Open-Existing([string] $Name, [string] $Dir, [string] $Url) {
    if (-not (Test-Path -LiteralPath (Join-Path $Dir '.git'))) {
        Write-Status fail $Name 'Exists but is not a git repository'
        return $null
    }
    $remote = Find-Remote $Dir $Url
    if (-not $remote) {
        Write-Status fail $Name "No remote points at $Url"
        return $null
    }
    # Discard stdout: anything git printed here would become part of this function's return value.
    & git -C $Dir fetch --quiet $remote | Out-Null
    if ($LASTEXITCODE -ne 0) {
        Write-Status fail $Name "Fetch from $remote failed"
        return $null
    }
    return $remote
}

# ── sync ─────────────────────────────────────────────────────────────────────

function Invoke-FastForward([string] $Name, [string] $Dir, [string] $Remote, [string] $Want, [string] $Label) {
    $tracking = "$Remote/$Want"
    if (-not (Test-Git -C $Dir rev-parse -q --verify "refs/remotes/$tracking")) {
        Write-Status note $Name "$tracking not found, left alone"
        return
    }
    $behind = [int](Get-Git -C $Dir rev-list --count "HEAD..$tracking")
    $ahead = [int](Get-Git -C $Dir rev-list --count "$tracking..HEAD")
    if ($behind -eq 0 -and $ahead -eq 0) {
        Write-Status ok $Name "Up to date on $Label"
    }
    elseif ($ahead -eq 0) {
        & git -C $Dir merge --ff-only --quiet $tracking
        Write-Status ok $Name "Fast-forwarded $Label to $(Get-Short (Get-Head $Dir))"
    }
    elseif ($behind -eq 0) {
        Write-Status note $Name "$Want is ahead of $tracking by $ahead, left alone"
    }
    else {
        Write-Status note $Name "$Want diverged from $tracking, not merged"
    }
}

function Sync-Owned([string] $Name, [string] $Dir, [string] $Url) {
    $default = Get-DefaultBranch $Url
    if (-not $default) {
        Write-Status fail $Name "Cannot reach $Url"
        return
    }
    $want = $default
    $matched = $false
    if ($MatchBranch -and (Test-RemoteBranch $Url $MatchBranch)) {
        $want = $MatchBranch
        $matched = $true
    }
    $label = if ($matched) { "$want (matched)" } else { $want }

    if (-not (Test-Path -LiteralPath $Dir)) {
        & git clone --quiet -c core.longpaths=true --branch $want $Url $Dir
        if ($LASTEXITCODE -eq 0) { Write-Status ok $Name "Cloned on $label" }
        else { Write-Status fail $Name 'Clone failed' }
        return
    }

    $remote = Open-Existing $Name $Dir $Url
    if (-not $remote) { return }
    if (Test-Dirty $Dir) {
        Write-Status note $Name 'Uncommitted changes, left alone'
        return
    }
    $current = Get-CurrentBranch $Dir
    if ($current -ceq $want) {
        Invoke-FastForward $Name $Dir $remote $want $label
    }
    elseif ($matched) {
        if (Test-LocalOnly $Dir) {
            Write-Status note $Name 'Has commits on no remote, left alone'
            return
        }
        if (Test-Git -C $Dir rev-parse -q --verify "refs/heads/$want") {
            & git -C $Dir checkout --quiet $want
            if ($LASTEXITCODE -eq 0) { & git -C $Dir merge --ff-only --quiet "$remote/$want" 2>$null }
        }
        else {
            & git -C $Dir checkout --quiet -b $want --track "$remote/$want"
        }
        if ((Get-CurrentBranch $Dir) -ceq $want) {
            Write-Status ok $Name "Switched to $label at $(Get-Short (Get-Head $Dir))"
        }
        else {
            Write-Status fail $Name "Could not switch to $want"
        }
    }
    elseif (-not $current) {
        Write-Status note $Name 'Detached HEAD, left alone'
    }
    else {
        Write-Status note $Name "On branch $current, left alone"
    }
}

function Sync-Pinned([string] $Name, [string] $Dir, [string] $Url, [string] $Pin) {
    $s = Get-Short $Pin
    if (-not (Test-Path -LiteralPath $Dir)) {
        & git clone --quiet -c core.longpaths=true $Url $Dir
        if ($LASTEXITCODE -ne 0) {
            Write-Status fail $Name 'Clone failed'
            return
        }
        if ((Confirm-Commit $Dir origin $Pin) -and
            (Test-Git -C $Dir -c advice.detachedHead=false checkout --quiet --detach $Pin)) {
            Write-Status ok $Name "Cloned at pin $s"
        }
        else {
            Write-Status fail $Name "Pin $s not on $Url"
        }
        return
    }

    $remote = Open-Existing $Name $Dir $Url
    if (-not $remote) { return }
    if ((Get-Head $Dir) -eq $Pin) {
        Write-Status ok $Name "At pin $s"
    }
    elseif (Test-Dirty $Dir) {
        Write-Status fail $Name "Uncommitted changes, not moved to pin $s"
    }
    elseif (Test-LocalOnly $Dir) {
        Write-Status fail $Name "Has commits on no remote, not moved to pin $s"
    }
    elseif (-not (Confirm-Commit $Dir $remote $Pin)) {
        Write-Status fail $Name "Pin $s not on $Url"
    }
    elseif (Test-Git -C $Dir -c advice.detachedHead=false checkout --quiet --detach $Pin) {
        Write-Status ok $Name "Moved to pin $s"
    }
    else {
        Write-Status fail $Name "Could not check out pin $s"
    }
}

# ── check ────────────────────────────────────────────────────────────────────

function Test-Owned([string] $Name, [string] $Dir, [string] $Url) {
    if (-not (Test-Path -LiteralPath $Dir)) {
        Write-Status fail $Name 'Missing'
        return
    }
    $remote = Open-Existing $Name $Dir $Url
    if (-not $remote) { return }
    $want = Get-DefaultBranch $Url
    $label = $want
    if ($MatchBranch -and (Test-RemoteBranch $Url $MatchBranch)) {
        $want = $MatchBranch
        $label = "$want (matched)"
    }
    $tracking = "$remote/$want"
    $current = Get-CurrentBranch $Dir
    $level = 'ok'
    if (-not $current) {
        $level = 'note'; $msg = 'Detached HEAD'
    }
    elseif ($current -cne $want) {
        $level = 'note'; $msg = "On branch $current"
    }
    else {
        $behind = [int](Get-Git -C $Dir rev-list --count "HEAD..$tracking")
        $ahead = [int](Get-Git -C $Dir rev-list --count "$tracking..HEAD")
        if ($behind -eq 0 -and $ahead -eq 0) { $msg = "Up to date on $label" }
        elseif ($ahead -eq 0) { $level = 'note'; $msg = "Behind $tracking by $behind" }
        elseif ($behind -eq 0) { $level = 'note'; $msg = "Ahead of $tracking by $ahead" }
        else { $level = 'note'; $msg = "Diverged from $tracking" }
    }
    if (Test-Dirty $Dir) {
        $level = 'note'; $msg = "$msg, uncommitted changes"
    }
    Write-Status $level $Name $msg
}

function Test-Pinned([string] $Name, [string] $Dir, [string] $Url, [string] $Pin) {
    $s = Get-Short $Pin
    if (-not (Test-Path -LiteralPath $Dir)) {
        Write-Status fail $Name 'Missing'
        return
    }
    $remote = Open-Existing $Name $Dir $Url
    if (-not $remote) { return }
    if (-not (Confirm-Commit $Dir $remote $Pin) -or -not (Test-OnRemote $Dir $remote $Pin)) {
        Write-Status fail $Name "Pin $s not on $Url"
    }
    elseif ((Get-Head $Dir) -eq $Pin) {
        if (Test-Dirty $Dir) { Write-Status note $Name "At pin $s, uncommitted changes" }
        else { Write-Status ok $Name "At pin $s" }
    }
    else {
        $msg = "Not at pin $s"
        if (Test-Dirty $Dir) { $msg = "$msg, uncommitted changes" }
        Write-Status fail $Name $msg
    }
}

# ── record ───────────────────────────────────────────────────────────────────

$Records = [System.Collections.Generic.List[object]]::new()

function Save-Pinned([string] $Name, [string] $Dir, [string] $Url, [string] $Pin) {
    if (-not (Test-Path -LiteralPath $Dir)) {
        Write-Status fail $Name 'Missing'
        return
    }
    $remote = Open-Existing $Name $Dir $Url
    if (-not $remote) { return }
    $head = Get-Head $Dir
    if (Test-Dirty $Dir) {
        Write-Status fail $Name 'Uncommitted changes, not recorded'
    }
    elseif (-not (Test-OnRemote $Dir $remote $head)) {
        Write-Status fail $Name "$(Get-Short $head) is on no remote branch, not recorded"
    }
    elseif ($head -eq $Pin) {
        Write-Status ok $Name "Unchanged at $(Get-Short $head)"
    }
    else {
        $Records.Add([pscustomobject]@{ Old = $Pin; New = $head })
        Write-Status ok $Name "Pinned $(Get-Short $head), was $(Get-Short $Pin)"
    }
}

# ── Self-update ──────────────────────────────────────────────────────────────

# Fast-forwards this repo when it is clean and on its default branch. Returns $true when the
# script must start again: the manifest and this file may both have changed.
function Update-Self {
    if (-not (Test-Path -LiteralPath (Join-Path $Root '.git'))) { return $false }
    $current = Get-CurrentBranch $Root
    if (-not $current) {
        Write-Status note LSP 'Detached HEAD, not updated'
        return $false
    }
    $upstream = Get-Git -C $Root rev-parse --abbrev-ref --symbolic-full-name '@{u}'
    $remote = if ($upstream) { $upstream.Split('/')[0] } else { 'origin' }
    $default = Get-DefaultBranch $remote $Root
    if (-not $default) {
        Write-Status note LSP "Cannot reach $remote, not updated"
        return $false
    }
    if ($current -cne $default) {
        Write-Status note LSP "On branch $current, not updated"
        return $false
    }
    if (Test-Dirty $Root) {
        Write-Status note LSP 'Uncommitted changes, not updated'
        return $false
    }
    & git -C $Root fetch --quiet $remote
    if ($LASTEXITCODE -ne 0) {
        Write-Status note LSP "Fetch from $remote failed, not updated"
        return $false
    }
    if ((Get-Head $Root) -eq (Get-Git -C $Root rev-parse "$remote/$default")) {
        Write-Status ok LSP "Up to date on $default"
        return $false
    }
    if (Test-Git -C $Root merge --ff-only --quiet "$remote/$default") {
        Write-Status ok LSP "Fast-forwarded $default to $(Get-Short (Get-Head $Root)), restarting"
        return $true
    }
    Write-Status note LSP "$default diverged from $remote/$default, not merged"
    return $false
}

# ── Main ─────────────────────────────────────────────────────────────────────

if ($Command -eq 'sync' -and -not $NoSelfUpdate) {
    if (Update-Self) {
        $restart = @($Command, '-Groups', ($Selected -join ','))
        if ($PSBoundParameters.ContainsKey('MatchBranch')) { $restart += @('-MatchBranch', $MatchBranch) }
        if ($PSBoundParameters.ContainsKey('Manifest')) { $restart += @('-Manifest', $Manifest) }
        if ($PSBoundParameters.ContainsKey('Summary')) { $restart += @('-Summary', $Summary) }
        $restart += '-NoSelfUpdate'
        & (Get-Process -Id $PID).Path -NoProfile -NonInteractive -File (Join-Path $Root 'setup-workspace.ps1') @restart
        exit $LASTEXITCODE
    }
}

foreach ($row in $Rows) {
    if ($row.Group -cnotin $Selected) { continue }
    $dir = Join-Path $Root $row.Path
    $owned = $row.Ref -ceq 'default'
    switch ($Command) {
        'sync' { if ($owned) { Sync-Owned $row.Name $dir $row.Url } else { Sync-Pinned $row.Name $dir $row.Url $row.Ref } }
        'check' { if ($owned) { Test-Owned $row.Name $dir $row.Url } else { Test-Pinned $row.Name $dir $row.Url $row.Ref } }
        'record' { if (-not $owned) { Save-Pinned $row.Name $dir $row.Url $row.Ref } }
    }
}

if ($Command -eq 'record') {
    if ($script:Failed) {
        [Console]::Error.WriteLine("$(Split-Path -Leaf $Manifest) not changed")
    }
    elseif ($Records.Count -gt 0) {
        $text = [IO.File]::ReadAllText($Manifest)
        foreach ($r in $Records) { $text = $text.Replace($r.Old, $r.New) }
        [IO.File]::WriteAllText($Manifest, $text)
    }
}

if ($Summary) {
    $block = "### Workspace`n`n" + '```text' + "`n" + (($script:Out | ForEach-Object { "$_`n" }) -join '') + '```' + "`n`n"
    [IO.File]::AppendAllText($Summary, $block)
}

exit ([int]$script:Failed)
