# Publishes the WebGL build in Builds/WebGL to GitHub Pages as a numbered release. The gh-pages branch
# holds only builds, one folder per release, and a front page that opens the latest:
#
#   https://mcraigtyler.github.io/ReCompile/                  the latest release
#   https://mcraigtyler.github.io/ReCompile/release/v0.0.1/   that release, for good
#
# The release number is the project's version (Player Settings > Version, shown on the title screen), and the
# build must have been made with it. Releases come from main (checked out, and the same as GitHub's), and that
# commit is tagged (v0.0.1). Everything is public once pushed.
#
# Usage:  powershell -File tools/publish-pages.ps1 [-Version 0.0.1] [-NoPush] [-Force]
#   -Version  the release number (default: the project's version); must match the build's
#   -NoPush   commit and tag, but don't push, to look it over first (then: git push origin gh-pages v0.0.1)
#   -Force    replace a release that's already published, and move its tag
#
# Build first (File > Build Profiles > WebGL > Build, to Builds/WebGL). Once, in the repo on GitHub:
# Settings > Pages > Deploy from a branch > gh-pages, / (root).
param(
    [string]$Version,
    [switch]$NoPush,
    [switch]$Force
)

# Failures are caught by exit code (Invoke-Git): in Windows PowerShell, 'Stop' would also stop on anything
# git merely prints to stderr.
$ErrorActionPreference = 'Continue'
$root = Resolve-Path "$PSScriptRoot/.."
$build = Join-Path $root 'Builds/WebGL'
$branch = 'gh-pages'
$site = 'https://mcraigtyler.github.io/ReCompile'

function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments = $true)] $GitArgs)
    & git.exe @GitArgs
    if ($LASTEXITCODE -ne 0) { throw "git $($GitArgs -join ' ') failed" }
}

function Stop-Publish([string]$message) {
    Write-Host $message -ForegroundColor Red
    exit 1
}

# ---------- What's being published ----------

$page = Join-Path $build 'index.html'
if (-not (Test-Path $page)) { Stop-Publish "No build at $build. Build the WebGL profile to Builds/WebGL first." }

if (-not $Version) {
    $settings = Get-Content (Join-Path $root 'ProjectSettings/ProjectSettings.asset') -Raw
    if ($settings -match '(?m)^\s*bundleVersion:\s*(\S+)') { $Version = $Matches[1] }
}
$Version = "$Version".TrimStart('v')
if ($Version -notmatch '^\d+\.\d+\.\d+$') { Stop-Publish "'$Version' isn't a release number like 0.0.1. Set it in Player Settings > Version, or pass -Version." }

$built = if ((Get-Content $page -Raw) -match 'productVersion:\s*"([^"]*)"') { $Matches[1] } else { $null }
if ($built -ne $Version) {
    Stop-Publish "The build in Builds/WebGL is version '$built', not $Version. Rebuild it after setting the version (Player Settings > Version)."
}

$tag = "v$Version"
$release = "release/$tag"

Push-Location $root
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("recompile-pages-" + [guid]::NewGuid().ToString('N').Substring(0, 8))
try {
    # Releases come from main, as it is on GitHub, so every tag is on main.
    $current = (& git.exe rev-parse --abbrev-ref HEAD).Trim()
    if ($current -ne 'main') { Stop-Publish "Publish from main (this is $current). Merge your branch, then: git checkout main; git pull" }
    Invoke-Git fetch --quiet origin main
    $source = (& git.exe rev-parse HEAD).Trim()
    if ($source -ne (& git.exe rev-parse origin/main).Trim()) {
        Stop-Publish "Your main isn't the same as GitHub's. Pull (or push) first, so the tag goes on a commit that's on GitHub."
    }
    $short = $source.Substring(0, 7)
    if (& git.exe status --porcelain --untracked-files=no) {
        Write-Host "Note: there are uncommitted changes, so the build may not match commit $short exactly." -ForegroundColor Yellow
    }

    # The tag goes on the commit the build came from.
    $tagged = & git.exe rev-parse -q --verify "refs/tags/$tag^{commit}"
    if ($tagged -and $tagged.Trim() -ne $source -and -not $Force) {
        Stop-Publish "Tag $tag is already on another commit ($($tagged.Trim().Substring(0, 7))). Raise the version, or pass -Force to move it."
    }

    # A separate worktree on gh-pages, so the checkout you work in isn't touched.
    & git.exe ls-remote --exit-code --heads origin $branch | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Invoke-Git fetch --quiet origin $branch
        Invoke-Git worktree add --quiet -B $branch $work "origin/$branch"
    } else {
        Write-Host "No $branch branch yet: starting one."
        Invoke-Git worktree add --quiet --detach $work HEAD
        Invoke-Git -C $work checkout --quiet --orphan $branch
        Invoke-Git -C $work rm -r -q -f --ignore-unmatch .
        Get-ChildItem -Force $work | Where-Object { $_.Name -ne '.git' } | Remove-Item -Recurse -Force
    }

    # ---------- The release folder ----------

    $folder = Join-Path $work $release
    if (Test-Path $folder) {
        if (-not $Force) { Stop-Publish "$tag is already published. Raise the version, or pass -Force to replace it." }
        Remove-Item -Recurse -Force $folder
    }
    New-Item -ItemType Directory -Force $folder | Out-Null
    Copy-Item -Recurse -Force (Join-Path $build '*') $folder

    # ---------- The front page: straight to the latest release ----------

    $front = @"
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8">
  <title>ReCompile</title>
  <meta http-equiv="refresh" content="0; url=$release/">
  <link rel="canonical" href="$release/">
  <script>location.replace("$release/" + location.search + location.hash);</script>
</head>
<body style="background:#05040a;color:#e6e0f0;font-family:sans-serif">
  <p>Opening <a href="$release/" style="color:#e58bff">ReCompile $tag</a>...</p>
</body>
</html>
"@
    [System.IO.File]::WriteAllText((Join-Path $work 'index.html'), $front, (New-Object System.Text.UTF8Encoding $false))
    New-Item -ItemType File -Force (Join-Path $work '.nojekyll') | Out-Null
    # Stored byte for byte: no line-ending changes to the pages or the build files.
    Set-Content -Encoding ascii -Path (Join-Path $work '.gitattributes') -Value '* -text'

    # ---------- Commit and tag ----------

    Invoke-Git -C $work add -A
    $size = "{0:N1} MB" -f ((Get-ChildItem -Recurse -File $folder | Measure-Object Length -Sum).Sum / 1MB)
    Invoke-Git -C $work commit --quiet -m "Publish $tag (WebGL build of $short, $size)"
    if ($Force -or -not $tagged) { Invoke-Git tag -f -a $tag $source -m "ReCompile $tag" | Out-Null }
    $releases = (Get-ChildItem -Directory (Join-Path $work 'release') | ForEach-Object Name) -join ', '
    Write-Host "Committed $tag to $branch ($size), tagged $tag on $short. Releases on the site: $releases."

    if ($NoPush) {
        Write-Host "Not pushed. To publish it: git push origin $branch $tag"
    } else {
        Invoke-Git -C $work push --quiet origin $branch
        if ($Force) { Invoke-Git push --quiet --force origin "refs/tags/$tag" } else { Invoke-Git push --quiet origin "refs/tags/$tag" }
        Write-Host "Published. Within a minute or two: $site/ (and $site/$release/)."
    }
}
finally {
    if (Test-Path $work) { & git.exe worktree remove --force $work | Out-Null }
    & git.exe worktree prune | Out-Null
    Pop-Location
}
