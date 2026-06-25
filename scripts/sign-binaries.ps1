# sign-binaries.ps1
# Automates self-signed code signing for Wallpaper Turbo local development and publish.
# Registers the certificate in the Current User stores to establish trust without admin prompt.

param(
    [string] $TargetDir = "",
    [string] $FilePath = "",
    [switch] $SkipRootTrust = $false
)

$ErrorActionPreference = "Stop"
$Subject = "CN=COSMO-ARNAB"

# 1. Resolve or create code signing certificate
Write-Host "[Sign] Resolving code signing certificate for '$Subject'..."
$cert = Get-ChildItem -Path Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $Subject } | Select-Object -First 1

if ($null -eq $cert) {
    Write-Host "[Sign] Certificate not found. Generating a new self-signed code-signing certificate..."
    $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject $Subject -KeySpec Signature -KeyExportPolicy Exportable -KeyUsage DigitalSignature -KeyUsageProperty Sign -CertStoreLocation "Cert:\CurrentUser\My"
    Write-Host "[Sign] Created new certificate: $($cert.Thumbprint)"
} else {
    Write-Host "[Sign] Found existing certificate: $($cert.Thumbprint)"
}

# 2. Register certificate in Trusted Root Certification Authorities for Current User to establish trust
Write-Host "[Sign] Ensuring certificate is trusted locally by Current User..."
$rootStore = New-Object System.Security.Cryptography.X509Certificates.X509Store("Root", "CurrentUser")
$rootStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
$existingRoot = $rootStore.Certificates | Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

if ($null -eq $existingRoot) {
    if ($SkipRootTrust) {
        Write-Host "[Sign] SkipRootTrust switch is set. Skipping adding certificate to Trusted Root store."
    } else {
        Write-Host "[Sign] Adding certificate to CurrentUser Trusted Root store..."
        try {
            $rootStore.Add($cert)
            Write-Host "[Sign] Certificate successfully added to Trusted Root store."
        } catch {
            Write-Warning "[Sign] Failed to add certificate to Trusted Root store (this is expected in non-interactive/CI environments). Run the script with -SkipRootTrust to bypass this. Error: $_"
        }
    }
} else {
    Write-Host "[Sign] Certificate is already in CurrentUser Trusted Root store."
}
$rootStore.Close()

# 3. Perform signing
$filesToSign = New-Object System.Collections.Generic.List[string]

if (-not [string]::IsNullOrEmpty($TargetDir)) {
    $resolvedDir = [System.IO.Path]::GetFullPath($TargetDir)
    if (Test-Path -Path $resolvedDir) {
        Write-Host "[Sign] Gathering executables from directory: $resolvedDir"
        Get-ChildItem -Path $resolvedDir -Filter "*.exe" -Recurse | ForEach-Object {
            $filesToSign.Add($_.FullName)
        }
    }
}

if (-not [string]::IsNullOrEmpty($FilePath)) {
    $resolvedFile = [System.IO.Path]::GetFullPath($FilePath)
    if (Test-Path -Path $resolvedFile) {
        $filesToSign.Add($resolvedFile)
    }
}

if ($filesToSign.Count -eq 0) {
    Write-Host "[Warning] No files found to sign."
    exit 0
}

Write-Host "[Sign] Signing $($filesToSign.Count) file(s)..."
foreach ($file in $filesToSign) {
    Write-Host "[Sign] Signing: $file"
    $sig = Set-AuthenticodeSignature -FilePath $file -Certificate $cert
    if ($sig.Status -eq "Valid") {
        Write-Host "[Sign] Success: $file is now signed."
    } else {
        Write-Warning "[Sign] Warning: Signature status is $($sig.Status) for $file. Error details: $($sig.StatusMessage)"
    }
}

Write-Host "[Sign] Code signing completed successfully."
exit 0
