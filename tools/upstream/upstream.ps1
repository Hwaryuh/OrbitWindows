# Follows the upstream repositories listed in tools/upstream/upstreams.json (Orbit,
# Vorssaint). The clones under .refs/ stay at their pinned commit; this script fetches, reports what
# changed upstream since the pin (grouped by the areas in the manifest), and moves a pin forward
# only when asked.
#   powershell -File tools/upstream/upstream.ps1                         fetch every upstream, show state
#   powershell -File tools/upstream/upstream.ps1 -Mode report            write docs/upstream/<date>-<name>.md
#   powershell -File tools/upstream/upstream.ps1 -Mode advance -Name orbit           dry run
#   powershell -File tools/upstream/upstream.ps1 -Mode advance -Name orbit -Apply    move the pin
# -Target <ref> (with one -Name) reports or advances to that ref instead of the manifest's rule.
# Vorssaint is GPL-3.0: its code is read for behavior only and never copied into this repository.
param(
    [ValidateSet('fetch', 'report', 'advance')][string]$Mode = 'fetch',
    [string[]]$Name,
    [string]$Target,
    [switch]$Apply
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false

$Root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$ManifestPath = Join-Path $PSScriptRoot 'upstreams.json'
$Utf8 = New-Object System.Text.UTF8Encoding $false
$GplNote = 'GPL-3.0: read for behavior only. Do not copy its code, comments or strings into this repository.'

function Get-Prop($Object, [string]$Key) {
    $p = $Object.PSObject.Properties[$Key]
    if ($null -eq $p) { return $null }
    return $p.Value
}

function Invoke-Git([string]$Dir, [string[]]$Arguments, [switch]$AllowFail) {
    # Failure is judged by the exit code alone; a warning on stderr must not stop the script.
    $saved = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = & git -C $Dir -c core.quotepath=off @Arguments
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $saved
    }
    $script:GitExit = $code
    if ($code -ne 0 -and -not $AllowFail) {
        throw "git $($Arguments -join ' ') failed in $Dir (exit $code)"
    }
    return $output
}

# Output of a -z command as tokens (NUL-separated, trailing empty token dropped).
function Split-Nul($Output) {
    $text = (@($Output) -join "`n")
    if ($text -eq '') { return @() }
    $tokens = $text.Split([char]0)
    if ($tokens.Length -ge 2 -and $tokens[$tokens.Length - 1] -eq '') {
        $tokens = $tokens[0..($tokens.Length - 2)]
    }
    return $tokens
}

function Read-Manifest {
    $text = [System.IO.File]::ReadAllText($ManifestPath, $Utf8)
    return @{ Text = $text; Data = ($text | ConvertFrom-Json) }
}

function Select-Upstreams($Manifest) {
    $all = @($Manifest.Data.upstreams)
    if (-not $Name) { return $all }
    $picked = @()
    foreach ($n in $Name) {
        $u = $all | Where-Object { $_.name -eq $n }
        if (-not $u) { throw "Unknown upstream '$n'. Known: $(($all | ForEach-Object { $_.name }) -join ', ')" }
        $picked += $u
    }
    return $picked
}

function Get-CloneDir($Upstream) { return (Join-Path $Root $Upstream.dir) }

function Short([string]$Sha) { return $Sha.Substring(0, 7) }

function Test-Clone($Upstream) {
    $dir = Get-CloneDir $Upstream
    if (-not (Test-Path (Join-Path $dir '.git'))) { return "no clone at $($Upstream.dir); run -Mode fetch" }
    $url = (Invoke-Git $dir @('remote', 'get-url', 'origin') -AllowFail)
    if ($script:GitExit -ne 0) { return "$($Upstream.dir) has no origin remote" }
    $want = $Upstream.url.TrimEnd('/') -replace '\.git$', ''
    $have = ([string]$url).Trim().TrimEnd('/') -replace '\.git$', ''
    if ($have -ne $want) { return "$($Upstream.dir) points to $have, manifest says $want" }
    return $null
}

function Get-Head([string]$Dir) { return ([string](Invoke-Git $Dir @('rev-parse', 'HEAD'))).Trim() }

function Test-Dirty([string]$Dir) {
    $changed = @(Invoke-Git $Dir @('status', '--porcelain')) | Where-Object { $_ }
    return [bool]$changed
}

function Resolve-Target($Upstream) {
    $dir = Get-CloneDir $Upstream
    $ref = $null
    if ($Target) {
        $ref = $Target
    } elseif ($Upstream.follow -eq 'branch') {
        $ref = "origin/$($Upstream.branch)"
    } elseif ($Upstream.follow -like 'tag:*') {
        $glob = $Upstream.follow.Substring(4)
        $tags = @(Invoke-Git $dir @('tag', '-l', $glob, '--sort=-v:refname'))
        # A hyphen marks a prerelease (v3.4.2-beta.2); the newest stable version wins.
        $stable = @($tags | Where-Object { $_ -and $_ -notmatch '-' })
        if ($stable.Count -eq 0) { throw "$($Upstream.name): no stable tag matches '$glob'" }
        $ref = $stable[0]
    } else {
        throw "$($Upstream.name): unknown follow rule '$($Upstream.follow)'"
    }
    $sha = Invoke-Git $dir @('rev-parse', '--verify', '-q', "$ref^{commit}") -AllowFail
    if ($script:GitExit -ne 0 -or -not $sha) { throw "$($Upstream.name): cannot resolve '$ref'; run -Mode fetch" }
    $date = Invoke-Git $dir @('show', '-s', '--format=%cs', ([string]$sha).Trim())
    return @{ Ref = $ref; Sha = ([string]$sha).Trim(); Date = ([string]$date).Trim() }
}

# Longest manifest path that covers a file. A trailing slash is a directory, otherwise one file.
function Find-Area($Areas, [string]$Path) {
    $best = $null
    $bestLength = -1
    foreach ($a in $Areas) {
        $p = [string]$a.path
        $hit = $false
        if ($p.EndsWith('/')) { $hit = $Path.StartsWith($p, [System.StringComparison]::Ordinal) }
        else { $hit = ($Path -ceq $p) }
        if ($hit -and $p.Length -gt $bestLength) { $best = $a; $bestLength = $p.Length }
    }
    return $best
}

# ---------------------------------------------------------------- fetch

function Invoke-Fetch($Upstream) {
    $dir = Get-CloneDir $Upstream
    $name = $Upstream.name
    if (-not (Test-Path $dir)) {
        Write-Host "${name}: cloning $($Upstream.url)"
        & git clone -q $Upstream.url $dir
        if ($LASTEXITCODE -ne 0) { throw "${name}: clone failed (exit $LASTEXITCODE)" }
        Invoke-Git $dir @('checkout', '-q', '--detach', $Upstream.pin) | Out-Null
    } else {
        $problem = Test-Clone $Upstream
        if ($problem) { Write-Warning "${name}: $problem. Skipped."; return }
        Invoke-Git $dir @('fetch', '-q', '--tags', '--prune', 'origin') | Out-Null
    }

    $head = Get-Head $dir
    $dirty = Test-Dirty $dir
    $pinKnown = Invoke-Git $dir @('rev-parse', '--verify', '-q', "$($Upstream.pin)^{commit}") -AllowFail
    if ($script:GitExit -ne 0 -or -not $pinKnown) {
        Write-Warning "${name}: pin $(Short $Upstream.pin) is not in the clone (history rewritten upstream?)."
        return
    }
    $branch = "origin/$($Upstream.branch)"
    $ahead = ([string](Invoke-Git $dir @('rev-list', '--count', "$($Upstream.pin)..$branch"))).Trim()
    $target = Resolve-Target $Upstream
    $line = "${name}: pin $(Short $Upstream.pin) ($($Upstream.pinDate)); $branch is $ahead commits ahead; target $($target.Ref) $(Short $target.Sha)"
    if ($target.Sha -eq $Upstream.pin) { $line += ' (up to date)' }
    Write-Host $line
    if ($head -ne $Upstream.pin) { Write-Warning "${name}: HEAD is $(Short $head), not the pin. Left as is." }
    if ($dirty) { Write-Warning "${name}: the clone has local changes. .refs/ is read-only; restore it." }
}

# ---------------------------------------------------------------- report

function Get-Changes($Dir, [string]$From, [string]$To) {
    $stats = @{}
    $tokens = @(Split-Nul (Invoke-Git $Dir @('diff', '--no-ext-diff', '--no-textconv', '--numstat', '-z', '-M', $From, $To)))
    $i = 0
    while ($i -lt $tokens.Length) {
        # "added<TAB>deleted<TAB>path", or "added<TAB>deleted<TAB>" then old and new path for a rename.
        $parts = $tokens[$i].Split([char[]]@("`t"), 3)
        if ($parts.Length -ge 3 -and $parts[2] -eq '') {
            $path = $tokens[$i + 2]
            $i += 3
        } else {
            $path = $parts[2]
            $i += 1
        }
        $stats[$path] = @{ Added = $parts[0]; Deleted = $parts[1] }
    }

    $changes = New-Object System.Collections.Generic.List[object]
    $tokens = @(Split-Nul (Invoke-Git $Dir @('diff', '--no-ext-diff', '--no-textconv', '--name-status', '-z', '-M', $From, $To)))
    $i = 0
    while ($i -lt $tokens.Length) {
        $status = $tokens[$i]
        $old = $null
        if ($status -match '^[RC]') {
            $old = $tokens[$i + 1]
            $path = $tokens[$i + 2]
            $i += 3
        } else {
            $path = $tokens[$i + 1]
            $i += 2
        }
        $s = $stats[$path]
        $changes.Add([pscustomobject]@{
            Status  = $status.Substring(0, 1)
            Path    = $path
            Old     = $old
            Added   = $(if ($s) { $s.Added } else { '?' })
            Deleted = $(if ($s) { $s.Deleted } else { '?' })
        })
    }
    return $changes
}

# Commit subjects keyed by the files each commit touched.
function Get-CommitsByFile($Dir, [string]$From, [string]$To) {
    $byFile = @{}
    $current = $null
    foreach ($line in @(Invoke-Git $Dir @('log', '--format=@@%h%x09%s', '--name-only', "$From..$To"))) {
        if (-not $line) { continue }
        if ($line.StartsWith('@@')) { $current = $line.Substring(2); continue }
        if (-not $byFile.ContainsKey($line)) { $byFile[$line] = New-Object System.Collections.Generic.List[string] }
        if (-not $byFile[$line].Contains($current)) { $byFile[$line].Add($current) }
    }
    return $byFile
}

function Format-Change($Change) {
    $stat = if ($Change.Added -eq '-') { 'binary' } else { "+$($Change.Added) -$($Change.Deleted)" }
    if ($Change.Old) { return "- $($Change.Status) ``$($Change.Old)`` -> ``$($Change.Path)`` ($stat)" }
    return "- $($Change.Status) ``$($Change.Path)`` ($stat)"
}

function Invoke-Report($Upstream) {
    $name = $Upstream.name
    $problem = Test-Clone $Upstream
    if ($problem) { Write-Warning "${name}: $problem. Skipped."; return }
    $dir = Get-CloneDir $Upstream
    $pin = $Upstream.pin
    $target = Resolve-Target $Upstream
    if ($target.Sha -eq $pin) { Write-Host "${name}: up to date at $(Short $pin), no report."; return }

    Invoke-Git $dir @('merge-base', '--is-ancestor', $pin, $target.Sha) -AllowFail | Out-Null
    $diverged = ($script:GitExit -ne 0)

    $commits = @(Invoke-Git $dir @('log', '--reverse', '--format=%h%x09%cs%x09%s', "$pin..$($target.Sha)"))
    $tags = @(Invoke-Git $dir @('tag', '-l', '--sort=v:refname', '--merged', $target.Sha, '--no-merged', $pin)) | Where-Object { $_ }
    $changes = @(Get-Changes $dir $pin $target.Sha)
    $byFile = Get-CommitsByFile $dir $pin $target.Sha

    # Directories that did not exist at the pin, at their shallowest level.
    $oldDirs = @{}
    foreach ($d in @(Split-Nul (Invoke-Git $dir @('ls-tree', '-r', '-d', '--name-only', '-z', $pin)))) { $oldDirs[$d] = $true }
    $newDirs = New-Object System.Collections.Generic.List[string]

    # Group changes by area. A rename between areas is listed under both.
    $groups = [ordered]@{}
    $unmapped = New-Object System.Collections.Generic.List[object]
    foreach ($c in $changes) {
        $paths = @($c.Path)
        if ($c.Old) { $paths += $c.Old }
        $seen = @{}
        foreach ($p in $paths) {
            $area = Find-Area $Upstream.areas $p
            if (-not $area) { if (-not $seen.ContainsKey('?')) { $unmapped.Add($c); $seen['?'] = $true }; continue }
            if ($seen.ContainsKey($area.path)) { continue }
            $seen[$area.path] = $true
            if (-not $groups.Contains($area.path)) { $groups[$area.path] = @{ Area = $area; Changes = New-Object System.Collections.Generic.List[object] } }
            $groups[$area.path].Changes.Add($c)
        }
        if ($c.Status -eq 'A' -or $c.Status -eq 'R' -or $c.Status -eq 'C') {
            $segments = $c.Path.Split('/')
            $prefix = ''
            for ($k = 0; $k -lt $segments.Length - 1; $k++) {
                $prefix = if ($prefix) { "$prefix/$($segments[$k])" } else { $segments[$k] }
                if (-not $oldDirs.ContainsKey($prefix)) {
                    if (-not $newDirs.Contains("$prefix/")) { $newDirs.Add("$prefix/") }
                    break
                }
            }
        }
    }

    $range = "$pin..$($target.Sha)"
    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Upstream report: $name")
    $lines.Add('')
    $lines.Add("**Date:** $(Get-Date -Format 'yyyy-MM-dd')  ")
    $lines.Add("**Repository:** $($Upstream.url) ($($Upstream.license), policy: $($Upstream.policy))  ")
    $lines.Add("**Range:** ``$(Short $pin)`` ($($Upstream.pinDate), pin) to ``$(Short $target.Sha)`` ($($target.Ref), $($target.Date))  ")
    $lines.Add("**Commits:** $($commits.Count), **files changed:** $($changes.Count)  ")
    $tagText = if ($tags) { ($tags -join ', ') } else { 'none' }
    $lines.Add("**Tags in range:** $tagText")
    $lines.Add('')
    if ($Upstream.policy -eq 'behavior-only') { $lines.Add("> $GplNote"); $lines.Add('') }
    if ($diverged) {
        $lines.Add('> The pin is not an ancestor of the target: upstream history was rewritten. File changes compare the two end points; the commit list holds only commits reachable from the target.')
        $lines.Add('')
    }

    $order = @('ported', 'planned', 'later', 'info', 'skip')
    $lines.Add('## Summary')
    $lines.Add('')
    $lines.Add('| Status | Areas | Files |')
    $lines.Add('|---|---|---|')
    $lines.Add("| unmapped | - | $($unmapped.Count) |")
    foreach ($st in $order) {
        $g = @($groups.Values | Where-Object { $_.Area.status -eq $st })
        $files = 0
        foreach ($x in $g) { $files += $x.Changes.Count }
        $lines.Add("| $st | $($g.Count) | $files |")
    }
    $lines.Add('')

    if ($unmapped.Count -gt 0) {
        $lines.Add('## Unmapped')
        $lines.Add('')
        $lines.Add('No area in `tools/upstream/upstreams.json` covers these files. Add one, then run the report again.')
        $lines.Add('')
        foreach ($c in $unmapped) { $lines.Add((Format-Change $c)) }
        $lines.Add('')
    }
    if ($newDirs.Count -gt 0) {
        $lines.Add('## New directories')
        $lines.Add('')
        $lines.Add('Directories that did not exist at the pin. A new upstream feature usually starts here.')
        $lines.Add('')
        foreach ($d in $newDirs) {
            $area = Find-Area $Upstream.areas $d
            $where = if ($area) { "$($area.status), area ``$($area.path)``" } else { 'unmapped' }
            $lines.Add("- ``$d`` ($where)")
        }
        $lines.Add('')
    }

    $titles = @{ ported = 'Ported: check against our code'; planned = 'Planned: not built yet'; later = 'Later'; info = 'Info: read, nothing to port' }
    foreach ($st in @('ported', 'planned', 'later', 'info')) {
        $g = @($groups.Values | Where-Object { $_.Area.status -eq $st })
        if ($g.Count -eq 0) { continue }
        $lines.Add("## $($titles[$st])")
        $lines.Add('')
        foreach ($x in $g) {
            $a = $x.Area
            $lines.Add("### $($a.feature) (``$($a.path)``)")
            $lines.Add('')
            $ours = Get-Prop $a 'ours'
            $note = Get-Prop $a 'note'
            if ($ours) { $lines.Add("Ours: ``$ours``  ") }
            if ($note) { $lines.Add("Note: $note  ") }
            $lines.Add("Diff: ``git -C $($Upstream.dir) diff $(Short $pin)..$(Short $target.Sha) -- $($a.path)``")
            $lines.Add('')
            foreach ($c in $x.Changes) { $lines.Add((Format-Change $c)) }
            $subjects = New-Object System.Collections.Generic.List[string]
            foreach ($c in $x.Changes) {
                foreach ($p in @($c.Path, $c.Old)) {
                    if ($p -and $byFile.ContainsKey($p)) {
                        foreach ($s in $byFile[$p]) { if (-not $subjects.Contains($s)) { $subjects.Add($s) } }
                    }
                }
            }
            if ($subjects.Count -gt 0) {
                $lines.Add('')
                $lines.Add('Commits:')
                foreach ($s in $subjects) {
                    $pair = $s.Split("`t", 2)
                    $lines.Add("- ``$($pair[0])`` $($pair[1])")
                }
            }
            $lines.Add('')
        }
    }

    $skipped = @($groups.Values | Where-Object { $_.Area.status -eq 'skip' })
    if ($skipped.Count -gt 0) {
        $lines.Add('## Skip')
        $lines.Add('')
        $lines.Add('| Area | Feature | Files |')
        $lines.Add('|---|---|---|')
        foreach ($x in $skipped) { $lines.Add("| ``$($x.Area.path)`` | $($x.Area.feature) | $($x.Changes.Count) |") }
        $lines.Add('')
    }

    $lines.Add('## Commits')
    $lines.Add('')
    foreach ($c in $commits) {
        $parts = ([string]$c).Split("`t", 3)
        if ($parts.Length -eq 3) { $lines.Add("- ``$($parts[0])`` $($parts[1]) $($parts[2])") }
    }
    $lines.Add('')
    $lines.Add('## After review')
    $lines.Add('')
    $lines.Add("Move the pin once the changes above are ported or ruled out: ``powershell -File tools/upstream/upstream.ps1 -Mode advance -Name $name -Apply``.")
    $lines.Add('')

    $outDir = Join-Path $Root 'docs\upstream'
    if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }
    $outFile = Join-Path $outDir "$(Get-Date -Format 'yyyy-MM-dd')-$name.md"
    [System.IO.File]::WriteAllText($outFile, ($lines -join "`n"), $Utf8)

    $portedFiles = 0
    foreach ($x in @($groups.Values | Where-Object { $_.Area.status -eq 'ported' })) { $portedFiles += $x.Changes.Count }
    Write-Host "${name}: $(Short $pin)..$(Short $target.Sha) ($($target.Ref)), $($commits.Count) commits, $portedFiles files in ported areas, $($unmapped.Count) unmapped -> $($outFile.Substring($Root.Length + 1))"
    if ($diverged) { Write-Warning "${name}: the pin is not an ancestor of the target." }
    if ($Upstream.policy -eq 'behavior-only') { Write-Host "  $GplNote" }
}

# ---------------------------------------------------------------- advance

function Invoke-Advance($Upstream) {
    $name = $Upstream.name
    $problem = Test-Clone $Upstream
    if ($problem) { Write-Warning "${name}: $problem. Skipped."; return }
    $dir = Get-CloneDir $Upstream
    $target = Resolve-Target $Upstream
    $oldPin = $Upstream.pin
    if ($target.Sha -eq $oldPin) { Write-Host "${name}: already at $(Short $oldPin)."; return }

    $plan = "${name}: pin $(Short $oldPin) ($($Upstream.pinDate)) -> $(Short $target.Sha) ($($target.Ref), $($target.Date))"
    if (-not $Apply) { Write-Host "$plan. Dry run; add -Apply to check out and save."; return }

    if (Test-Dirty $dir) { throw "${name}: the clone has local changes; not moving it." }

    # Rewrite the pin in the text so the hand-written layout stays, then check the result.
    $manifest = Read-Manifest
    $pattern = '"pin":\s*"' + $oldPin + '",(\s*)"pinDate":\s*"[^"]*"'
    $found = [regex]::Matches($manifest.Text, $pattern)
    if ($found.Count -ne 1) { throw "${name}: expected one pin entry for $oldPin in the manifest, found $($found.Count)." }
    $replacement = '"pin": "' + $target.Sha + '",$1"pinDate": "' + $target.Date + '"'
    $newText = [regex]::Replace($manifest.Text, $pattern, $replacement)
    $check = $newText | ConvertFrom-Json
    foreach ($u in $check.upstreams) {
        $before = $manifest.Data.upstreams | Where-Object { $_.name -eq $u.name }
        $expected = if ($u.name -eq $name) { $target.Sha } else { $before.pin }
        if ($u.pin -ne $expected) { throw "${name}: manifest check failed for $($u.name); nothing changed." }
    }

    # The new manifest goes to a side file first and replaces the old one in one step, so a failed
    # write never leaves a cut-off manifest behind.
    $temp = "$ManifestPath.new"
    $backup = "$ManifestPath.bak"
    [System.IO.File]::WriteAllText($temp, $newText, $Utf8)
    $oldHead = Get-Head $dir
    Invoke-Git $dir @('checkout', '-q', '--detach', $target.Sha) | Out-Null
    try {
        [System.IO.File]::Replace($temp, $ManifestPath, $backup)
        Remove-Item -LiteralPath $backup -Force -Confirm:$false
    } catch {
        $reason = $_.Exception.Message
        if (Test-Path -LiteralPath $temp) { Remove-Item -LiteralPath $temp -Force -Confirm:$false }
        Invoke-Git $dir @('checkout', '-q', '--detach', $oldHead) -AllowFail | Out-Null
        if ($script:GitExit -ne 0) {
            throw "${name}: could not save the manifest ($reason), and could not put the clone back at $(Short $oldHead). Check it out by hand."
        }
        throw "${name}: could not save the manifest ($reason); the clone is back at $(Short $oldHead)."
    }
    Write-Host "$plan. Done."
}

# ---------------------------------------------------------------- main

if ($Target -and @($Name).Count -ne 1) { throw '-Target needs exactly one -Name.' }
if ($Apply -and $Mode -ne 'advance') { throw '-Apply only applies to -Mode advance.' }

foreach ($u in @(Select-Upstreams (Read-Manifest))) {
    # Advance reads the manifest again for each upstream, so earlier pins stay saved.
    $fresh = (Read-Manifest).Data.upstreams | Where-Object { $_.name -eq $u.name }
    switch ($Mode) {
        'fetch' { Invoke-Fetch $fresh }
        'report' { Invoke-Report $fresh }
        'advance' { Invoke-Advance $fresh }
    }
}
