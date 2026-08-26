$ErrorActionPreference = "Stop"

$propsPath = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\Directory.Build.props"))
if (-not (Test-Path -LiteralPath $propsPath -PathType Leaf)) {
    throw "Version source not found: $propsPath"
}

[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$versionNodes = @($props.Project.PropertyGroup.Version | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($versionNodes.Count -ne 1) {
    throw "Directory.Build.props must contain exactly one Version value; found $($versionNodes.Count)."
}

$version = ([string]$versionNodes[0]).Trim()
if ($version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw "Invalid release version in Directory.Build.props: $version"
}

$projectPaths = @(
    "src\WallpaperTurbo.UI\WallpaperTurbo.UI.csproj",
    "src\WallpaperTurbo.Core\WallpaperTurbo.Core.csproj",
    "src\WallpaperTurbo.Updater\WallpaperTurbo.Updater.csproj",
    "src\WallpaperTurbo.AppRunner\WallpaperTurbo.AppRunner.csproj"
)
$versionPropertyNames = @("Version", "AssemblyVersion", "FileVersion", "InformationalVersion")
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))

foreach ($relativePath in $projectPaths) {
    $projectPath = Join-Path $repositoryRoot $relativePath
    [xml]$project = Get-Content -LiteralPath $projectPath -Raw
    foreach ($propertyName in $versionPropertyNames) {
        $overrides = @($project.Project.PropertyGroup.$propertyName | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if ($overrides.Count -gt 0) {
            throw "$relativePath overrides centrally managed property $propertyName. Keep release versions only in Directory.Build.props."
        }
    }
}
Write-Output $version

