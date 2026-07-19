# build-update-manifest.ps1
# UPD-015 Phase 1: Build a structured update.json manifest alongside the
# Inno Setup installer. The manifest is uploaded as a GitHub release asset
# by the human release publisher. Phase D.5 will automate this step.
#
# The sha256 below is the AUTHORITATIVE integrity value; UpdateCoordinator
# will use this hash instead of any body-parsed hash. The channel-driven
# default for min_signature_required is written here; the human can
# override the value by editing the produced update.json BEFORE upload.

param(
    [string] $Version = "",
    [ValidateSet("stable", "preview", "nightly")]
    [string] $Channel = "",
    [string] $InstallerPath = "setup\Wallpaper_Turbo_Setup.exe",
    [string] $OutputPath = "",
    [string] $RepoOwner = "",
    [string] $RepoName = "",
    [string] $ReleaseNotes = ""
)

$ErrorActionPreference = "Stop"
$Script:Tag = "[build-update-manifest]"

function Resolve-RepoOwner {
    param([string] $Default)
    if (-not [string]::IsNullOrWhiteSpace($env:WT_REPO_OWNER)) { return $env:WT_REPO_OWNER }
    if (-not [string]::IsNullOrWhiteSpace($Default)) { return $Default }
    $appXaml = Join-Path $PSScriptRoot "..\src\WallpaperTurbo.UI\App.xaml.cs"
    $appXaml = [System.IO.Path]::GetFullPath($appXaml)
    if (Test-Path -LiteralPath $appXaml) {
        $content = Get-Content -LiteralPath $appXaml -Raw
        $m = [regex]::Match($content, 'private\s+const\s+string\s+UpdateRepoOwner\s*=\s*"(?<v>[^"]+)"')
        if ($m.Success) { return $m.Groups["v"].Value }
    }
    return "COSMO-ARNAB"
}

function Resolve-RepoName {
    param([string] $Default)
    if (-not [string]::IsNullOrWhiteSpace($env:WT_REPO_NAME)) { return $env:WT_REPO_NAME }
    if (-not [string]::IsNullOrWhiteSpace($Default)) { return $Default }
    $appXaml = Join-Path $PSScriptRoot "..\src\WallpaperTurbo.UI\App.xaml.cs"
    $appXaml = [System.IO.Path]::GetFullPath($appXaml)
    if (Test-Path -LiteralPath $appXaml) {
        $content = Get-Content -LiteralPath $appXaml -Raw
        $m = [regex]::Match($content, 'private\s+const\s+string\s+UpdateRepoName\s*=\s*"(?<v>[^"]+)"')
        if ($m.Success) { return $m.Groups["v"].Value }
    }
    return "Wallpaper-Turbo"
}

function Resolve-Version {
    param([string] $Default)
    if (-not [string]::IsNullOrWhiteSpace($Default)) { return $Default }
    throw "Version is required. Resolve it from Directory.Build.props and pass -Version explicitly."
}

function Resolve-ChannelFromVersion {
    param([string] $VersionString)
    if ($VersionString -match "-(?<label>[A-Za-z0-9.-]+)$") {
        $label = $Matches["label"]
        if ($label -match "(?i)beta" -or $label -match "(?i)rc") { return "preview" }
        return "nightly"
    }
    return "stable"
}

function Resolve-ReleaseNotes {
    param([string] $Default)
    if (-not [string]::IsNullOrWhiteSpace($Default)) { return $Default }
    $notesPath = Join-Path $PSScriptRoot "..\release_notes.md"
    $notesPath = [System.IO.Path]::GetFullPath($notesPath)
    if (Test-Path -LiteralPath $notesPath) {
        return (Get-Content -LiteralPath $notesPath -Raw).TrimEnd()
    }
    return ""
}

# ---------------------------------------------------------------------------
# Resolve parameters
# ---------------------------------------------------------------------------
$InstallerPath = [System.IO.Path]::GetFullPath($InstallerPath)
if ([string]::IsNullOrEmpty($OutputPath)) {
    $OutputPath = Join-Path (Split-Path -Parent $InstallerPath) "update.json"
} else {
    $OutputPath = [System.IO.Path]::GetFullPath($OutputPath)
}

if ([string]::IsNullOrWhiteSpace($Version)) { $Version = Resolve-Version -Default "" }
if ([string]::IsNullOrWhiteSpace($Channel)) { $Channel = Resolve-ChannelFromVersion -VersionString $Version }
if ([string]::IsNullOrWhiteSpace($RepoOwner)) { $RepoOwner = Resolve-RepoOwner -Default "" }
if ([string]::IsNullOrWhiteSpace($RepoName)) { $RepoName = Resolve-RepoName -Default "" }
$ReleaseNotes = Resolve-ReleaseNotes -Default $ReleaseNotes

# ---------------------------------------------------------------------------
# Step 1: validate installer exists
# ---------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $InstallerPath)) {
    [Console]::Error.WriteLine("$Script:Tag installer not found: $InstallerPath")
    exit 1
}

# ---------------------------------------------------------------------------
# Step 2: SHA256 of installer
# ---------------------------------------------------------------------------
$hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $InstallerPath).Hash.ToLowerInvariant()

# ---------------------------------------------------------------------------
# Step 3: file size
# ---------------------------------------------------------------------------
$size = (Get-Item -LiteralPath $InstallerPath).Length

# ---------------------------------------------------------------------------
# Step 4: derive download URL
# ---------------------------------------------------------------------------
$downloadUrl = "https://github.com/$RepoOwner/$RepoName/releases/download/v$Version/Wallpaper_Turbo_Setup.exe"

# ---------------------------------------------------------------------------
# Step 5: channel -> default signature requirement
# The human release publisher may edit this field in update.json before upload
# to relax (e.g. nightly -> no signature) or tighten (e.g. preview ->
# authenticode) the verifier requirement.
# ---------------------------------------------------------------------------
switch ($Channel.ToLowerInvariant()) {
    "stable"  { $minSig = "sha256-only" }
    "preview" { $minSig = "sha256-only" }
    "nightly" { $minSig = "sha256-only" }
    default   { $minSig = "sha256-only" }
}

# ---------------------------------------------------------------------------
# Step 6: compose JSON in the exact field order/casing of the schema example
# ---------------------------------------------------------------------------
$generatedAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")

$obj = [ordered]@{
    schema_version         = 1
    generated_at           = $generatedAt
    version                = $Version
    channel                = $Channel
    release_notes          = $ReleaseNotes
    installer_filename     = "Wallpaper_Turbo_Setup.exe"
    download_url           = $downloadUrl
    sha256                 = $hash
    file_size_bytes        = $size
    min_supported_version  = "1.0.0"
    min_signature_required = $minSig
    rollback_eligible      = $false
}

$json = $obj | ConvertTo-Json -Depth 10

# ---------------------------------------------------------------------------
# Step 7: write JSON
# ---------------------------------------------------------------------------
$outputDir = Split-Path -Parent $OutputPath
if (-not [string]::IsNullOrEmpty($outputDir) -and -not (Test-Path -LiteralPath $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
}
Set-Content -LiteralPath $OutputPath -Value $json -Encoding UTF8

# ---------------------------------------------------------------------------
# Step 8: read-back sanity log
# ---------------------------------------------------------------------------
$readBack = Get-Content -LiteralPath $OutputPath -Raw
$readBackSize = (Get-Item -LiteralPath $OutputPath).Length
$readBackHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $OutputPath).Hash.ToLowerInvariant()
[Console]::Error.WriteLine("$Script:Tag wrote $OutputPath (size=$readBackSize sha256=$readBackHash)")
[Console]::Error.WriteLine("$Script:Tag installer's sha256 is $hash for $InstallerPath")

# ---------------------------------------------------------------------------
# Step 9: exit cleanly
# ---------------------------------------------------------------------------
exit 0
