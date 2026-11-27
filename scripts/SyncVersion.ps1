param(
    [string]$Version = "",
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $root "DynamicIsland.csproj"
$manifestPath = Join-Path $root "release-manifest.json"
$wingetDir = Join-Path $root "installer\winget"
$websitePath = Join-Path $root "website\index.html"

[xml]$csproj = Get-Content $csprojPath -Raw
$projVer = ($csproj.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version.Trim()
if ([string]::IsNullOrWhiteSpace($projVer)) {
    throw "Missing <Version> in DynamicIsland.csproj"
}

if ($VerifyOnly) {
    $manifest = Get-Content $manifestPath -Raw | ConvertFrom-Json
    if ([string]::IsNullOrWhiteSpace($manifest.version) -or [string]::IsNullOrWhiteSpace($manifest.releaseTag)) {
        throw "Missing 'version' or 'releaseTag' in release-manifest.json"
    }
    if ([string]::IsNullOrWhiteSpace($manifest.downloads.installer.sha256) -or
        [string]::IsNullOrWhiteSpace($manifest.downloads.portable.sha256)) {
        throw "Missing download SHA-256 hashes in release-manifest.json"
    }
    if ($manifest.version -ne $projVer) {
        throw "Version mismatch: DynamicIsland.csproj=$projVer vs release-manifest.json=$($manifest.version)"
    }

    if (Test-Path $wingetDir) {
        foreach ($yaml in Get-ChildItem $wingetDir -Filter *.yaml) {
            $match = Select-String -Path $yaml.FullName -Pattern '^PackageVersion:\s*(.+)$' | Select-Object -First 1
            if (-not $match) { throw "Missing PackageVersion in $($yaml.Name)" }
            $yamlVer = $match.Matches[0].Groups[1].Value.Trim()
            if ($yamlVer -ne $projVer) {
                throw "WinGet version mismatch in $($yaml.Name): $yamlVer vs $projVer"
            }
        }
    }

    if (Test-Path $websitePath) {
        $html = Get-Content $websitePath -Raw
        if ($html -notmatch "v$([regex]::Escape($projVer))" -or $html -notmatch "/releases/download/v$([regex]::Escape($projVer))/") {
            throw "website/index.html version mismatch: expected v$projVer"
        }
    }

    Write-Host "[OK] Verified DynamicIsland.csproj, release-manifest.json, WinGet manifests, and website/index.html match v$projVer."
    exit 0
}

if ([string]::IsNullOrWhiteSpace($Version)) {
    throw "Provide -Version <semver> or -VerifyOnly."
}

$cleanVersion = $Version.TrimStart('v', 'V')
$csprojRaw = Get-Content $csprojPath -Raw
$updatedCsproj = $csprojRaw -replace '<Version>[^<]+</Version>', "<Version>$cleanVersion</Version>"
Set-Content -Path $csprojPath -Value $updatedCsproj -Encoding utf8 -NoNewline

if (Test-Path $wingetDir) {
    foreach ($yaml in Get-ChildItem $wingetDir -Filter *.yaml) {
        $content = Get-Content $yaml.FullName -Raw
        $updatedYaml = $content -replace '(?m)^PackageVersion:\s*.+$', "PackageVersion: $cleanVersion"
        Set-Content -Path $yaml.FullName -Value $updatedYaml -Encoding utf8 -NoNewline
    }
}

if (Test-Path $websitePath) {
    $html = Get-Content $websitePath -Raw
    $html = $html -replace 'Get v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?', "Get v$cleanVersion"
    $html = $html -replace '<strong>v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?</strong>', "<strong>v$cleanVersion</strong>"
    $html = $html -replace 'Install v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?', "Install v$cleanVersion"
    $html = $html -replace '/releases/download/v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9.]+)?/', "/releases/download/v$cleanVersion/"
    Set-Content -Path $websitePath -Value $html -Encoding utf8 -NoNewline
}

Write-Host "[OK] Synced DynamicIsland.csproj, WinGet manifests, and website/index.html to v$cleanVersion."
