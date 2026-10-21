[CmdletBinding(DefaultParameterSetName = 'Package')]
param(
    [Parameter(Mandatory = $true, ParameterSetName = 'Package')][string]$PackagePath,
    [Parameter(Mandatory = $true, ParameterSetName = 'Layout')][string]$LayoutPath,
    [Parameter(ParameterSetName = 'Package')][string]$CertificateThumbprint,
    [Parameter(ParameterSetName = 'Package')][string]$WindowsSdkBin,
    [Parameter(ParameterSetName = 'Package')][switch]$SignOnly,
    [switch]$Launch
)

# Explicit developer deployment only. Does not change developer mode, machine
# certificate stores, execution policy, or any Windows security setting.
$ErrorActionPreference = 'Stop'
if ($PSCmdlet.ParameterSetName -eq 'Layout') {
    $LayoutPath = (Resolve-Path -LiteralPath $LayoutPath).Path
    $manifestPath = Join-Path $LayoutPath 'AppxManifest.xml'
    [xml]$manifest = [IO.File]::ReadAllText($manifestPath)
} else {
    $PackagePath = (Resolve-Path -LiteralPath $PackagePath).Path
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $entry = $zip.GetEntry('AppxManifest.xml')
        if (-not $entry) { throw 'MSIX has no AppxManifest.xml.' }
        $reader = [IO.StreamReader]::new($entry.Open())
        try { [xml]$manifest = $reader.ReadToEnd() } finally { $reader.Dispose() }
    } finally { $zip.Dispose() }
}
$identity = $manifest.Package.Identity
if ($identity.Name -ne 'LocalDev.Esp32MusicSync' -or $identity.Publisher -ne 'CN=ESP32 Music Sync Development') {
    throw 'This developer installer only accepts the explicit LocalDev.Esp32MusicSync identity. Use Store distribution for a real Store identity.'
}

if ($PSCmdlet.ParameterSetName -eq 'Layout') {
    $developerModeEnabled = $false
    foreach ($key in @('HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock', 'HKLM:\SOFTWARE\Policies\Microsoft\Windows\Appx')) {
        $setting = Get-ItemProperty -LiteralPath $key -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
        if ($setting -and $setting.AllowDevelopmentWithoutDevLicense -eq 1) { $developerModeEnabled = $true }
    }
    if (-not $developerModeEnabled) {
        throw 'Developer mode is not already enabled. Development layout registration requires an authorized development environment; this script does not enable it.'
    }
    if (-not (Test-Path -LiteralPath (Join-Path $LayoutPath 'Esp32MusicSync.exe'))) { throw 'Layout is missing Esp32MusicSync.exe.' }
    Add-AppxPackage -Register $manifestPath -ErrorAction Stop
    $outputDirectory = Split-Path -Parent $LayoutPath
    $signedPackage = $null
    $certificate = $null
} else {
    if (-not $WindowsSdkBin) {
        $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
        $sdkVersion = Get-ChildItem -LiteralPath $sdkRoot -Directory |
            Where-Object { $_.Name -match '^10\.0\.\d+\.0$' -and (Test-Path -LiteralPath (Join-Path $_.FullName 'x64\signtool.exe')) } |
            Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
        if (-not $sdkVersion) { throw 'Windows SDK x64 SignTool.exe is required.' }
        $WindowsSdkBin = Join-Path $sdkVersion.FullName 'x64'
    }
    $signTool = Join-Path $WindowsSdkBin 'signtool.exe'
    if (-not (Test-Path -LiteralPath $signTool)) { throw "Missing SignTool: $signTool" }

    $subject = [string]$identity.Publisher
    if ($CertificateThumbprint) {
        $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint" -ErrorAction Stop
        if ($certificate.Subject -ne $subject -or -not $certificate.HasPrivateKey) { throw 'Certificate subject or private key does not match the development package.' }
    } else {
        $certificate = Get-ChildItem -Path Cert:\CurrentUser\My |
            Where-Object { $_.Subject -eq $subject -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30) } |
            Sort-Object NotAfter -Descending | Select-Object -First 1
        if (-not $certificate) {
            $certificate = New-SelfSignedCertificate -Type Custom -Subject $subject `
                -FriendlyName 'ESP32 Music Sync local development' -CertStoreLocation Cert:\CurrentUser\My `
                -KeyAlgorithm RSA -KeyLength 2048 -KeyExportPolicy NonExportable -KeyUsage DigitalSignature `
                -HashAlgorithm SHA256 -NotAfter (Get-Date).AddYears(1) `
                -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
        }
    }
    $outputDirectory = Split-Path -Parent $PackagePath
    $publicCertificatePath = Join-Path $outputDirectory 'ESP32-MusicSync-Development.cer'
    Export-Certificate -Cert $certificate -FilePath $publicCertificatePath -Force | Out-Null
    $signedPackage = Join-Path $outputDirectory (([IO.Path]::GetFileNameWithoutExtension($PackagePath)) + '.signed.msix')
    Copy-Item -LiteralPath $PackagePath -Destination $signedPackage -Force
    & $signTool sign /fd SHA256 /sha1 $certificate.Thumbprint /s My $signedPackage
    if ($LASTEXITCODE -ne 0) { throw "SignTool failed with exit code $LASTEXITCODE" }
    $trustedForPackageInstallation = Test-Path -LiteralPath "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)"
    $signReport = [ordered]@{
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        SignedPackage = $signedPackage
        PackageSha256 = (Get-FileHash -LiteralPath $signedPackage -Algorithm SHA256).Hash
        CertificateThumbprint = $certificate.Thumbprint
        PublicCertificate = $publicCertificatePath
        CertificateStoresChanged = @('CurrentUser\My (only if a new development certificate was needed)')
        MachineTrustedPeopleContainsCertificate = $trustedForPackageInstallation
        PrivateKeyExported = $false
        Installed = $false
    }
    $signReport | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputDirectory 'development-sign-report.json') -Encoding utf8
    if ($SignOnly) {
        $signReport | ConvertTo-Json -Depth 4
        return
    }
    if (-not $trustedForPackageInstallation) {
        throw "Signed package created, but its certificate is not already trusted in LocalMachine\TrustedPeople. Windows requires machine trust for self-signed MSIX. No trust setting was changed. Public certificate: $publicCertificatePath. Use an authorized trusted development certificate or an already enabled development layout registration."
    }
    & $signTool verify /pa $signedPackage
    if ($LASTEXITCODE -ne 0) { throw 'Signature verification failed. No trust setting was changed.' }
    Add-AppxPackage -Path $signedPackage -ErrorAction Stop
}
$installed = Get-AppxPackage -Name 'LocalDev.Esp32MusicSync'
if (-not $installed) { throw 'Package registration was not found after deployment.' }
$report = [ordered]@{
    CreatedUtc = [DateTime]::UtcNow.ToString('o')
    Deployment = $PSCmdlet.ParameterSetName
    PackageFullName = $installed.PackageFullName
    PackageFamilyName = $installed.PackageFamilyName
    SignedPackage = $signedPackage
    CertificateThumbprint = $(if ($certificate) { $certificate.Thumbprint } else { $null })
    PrivateKeyExported = $false
    MachineTrustChanged = $false
    DeveloperModeChanged = $false
    StoreCertified = $false
}
$report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $outputDirectory 'development-install-report.json') -Encoding utf8
$report | ConvertTo-Json -Depth 4
if ($Launch) { & (Join-Path $PSScriptRoot 'launch-packaged.ps1') -IdentityName 'LocalDev.Esp32MusicSync' }
