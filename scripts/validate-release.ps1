param(
    [Parameter(Mandatory = $true)][string] $Version,
    [Parameter(Mandatory = $true)][string] $InstallerPath,
    [Parameter(Mandatory = $true)][string] $ManifestPath,
    [Parameter(Mandatory = $true)][string] $PublishDir
)

$ErrorActionPreference = "Stop"

function Normalize-Version([string] $value) {
    return ($value -replace '^v', '').Trim()
}

$expected = Normalize-Version $Version
if ($expected -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Invalid release version: $Version"
}

foreach ($path in @($InstallerPath, $ManifestPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing release artifact: $path" }
}

$uiPath = Join-Path $PublishDir "WallpaperTurbo.UI.exe"
$runnerPath = Join-Path $PublishDir "WallpaperTurbo.AppRunner.exe"
foreach ($path in @($uiPath, $runnerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing published executable: $path" }
}

$manifest = Get-Content -LiteralPath $ManifestPath -Raw | ConvertFrom-Json
$installer = Get-Item -LiteralPath $InstallerPath
$installerHash = (Get-FileHash -LiteralPath $InstallerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$installerVersion = Normalize-Version $installer.VersionInfo.ProductVersion

$checks = @(
    @{ Name = "manifest version"; Actual = Normalize-Version ([string]$manifest.version); Expected = $expected },
    @{ Name = "manifest download URL"; Actual = [string]$manifest.download_url; Expected = "https://github.com/COSMO-ARNAB/Wallpaper-Turbo/releases/download/v$expected/Wallpaper_Turbo_Setup.exe" },
    @{ Name = "installer product version"; Actual = $installerVersion; Expected = $expected },
    @{ Name = "manifest installer hash"; Actual = ([string]$manifest.sha256).ToLowerInvariant(); Expected = $installerHash },
    @{ Name = "manifest installer size"; Actual = [int64]$manifest.file_size_bytes; Expected = [int64]$installer.Length }
)

foreach ($check in $checks) {
    if ($check.Actual -ne $check.Expected) {
        throw "Release integrity check failed for $($check.Name): expected '$($check.Expected)', got '$($check.Actual)'."
    }
}

foreach ($path in @($uiPath, $runnerPath)) {
    $actual = Normalize-Version (Get-Item -LiteralPath $path).VersionInfo.ProductVersion
    if ($actual -ne $expected) { throw "Published executable version mismatch for ${path}: expected '$expected', got '$actual'." }
}

Write-Host "Release integrity validation passed for v$expected."
