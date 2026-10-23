<#
.SYNOPSIS
    Builds the NetPulse Optimizer installer: publish both executables, harvest them into
    one payload directory, build the MSI with WiX, and optionally sign everything.

.PARAMETER ProductVersion
    Four-part version (e.g. 0.1.0.0). Must increase on every release for MajorUpgrade to
    replace the previous install rather than refusing to downgrade.

.PARAMETER SigningThumbprint
    Optional. Thumbprint of a code-signing certificate in Cert:\CurrentUser\My to sign both
    executables and the final MSI with. Omit to produce an unsigned build (fine for local
    testing; real distribution needs a certificate from a trusted CA - see README.md in this
    folder).

.EXAMPLE
    .\build.ps1 -ProductVersion 0.1.0.0
    .\build.ps1 -ProductVersion 0.1.1.0 -SigningThumbprint 5B4442A937F7A79C376BE3DEA4C86956D29C4B77
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$ProductVersion,

    [string]$SigningThumbprint
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$installerDir = $PSScriptRoot
$publishDir = Join-Path $installerDir "publish\combined"

Write-Host "==> Publishing NetPulse.App (win-x64, framework-dependent)"
dotnet publish (Join-Path $repoRoot "src\NetPulse.App\NetPulse.App.csproj") -c Release -r win-x64 --self-contained false -o (Join-Path $installerDir "publish\app")
if ($LASTEXITCODE -ne 0) { throw "Publishing NetPulse.App failed." }

Write-Host "==> Publishing NetPulse.PrivilegedAgent.Host (win-x64, framework-dependent)"
dotnet publish (Join-Path $repoRoot "src\NetPulse.PrivilegedAgent.Host\NetPulse.PrivilegedAgent.Host.csproj") -c Release -r win-x64 --self-contained false -o (Join-Path $installerDir "publish\host")
if ($LASTEXITCODE -ne 0) { throw "Publishing NetPulse.PrivilegedAgent.Host failed." }

Write-Host "==> Merging into one payload directory (the two executables must sit side by side - ElevatedHelperOperationLauncher looks for the helper next to NetPulse.App.exe)"
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
Copy-Item (Join-Path $installerDir "publish\app") $publishDir -Recurse
Copy-Item (Join-Path $installerDir "publish\host\NetPulse.PrivilegedAgent.Host.*") $publishDir
Copy-Item (Join-Path $installerDir "publish\host\System.CodeDom.dll") $publishDir -ErrorAction SilentlyContinue

if ($SigningThumbprint) {
    Write-Host "==> Signing executables"
    & (Join-Path $installerDir "sign.ps1") -Thumbprint $SigningThumbprint -Path (Join-Path $publishDir "NetPulse.App.exe")
    & (Join-Path $installerDir "sign.ps1") -Thumbprint $SigningThumbprint -Path (Join-Path $publishDir "NetPulse.PrivilegedAgent.Host.exe")
}

Write-Host "==> Building the MSI"
$msiPath = Join-Path $installerDir "NetPulseOptimizer.msi"
if (Test-Path $msiPath) { Remove-Item $msiPath }
wix build (Join-Path $installerDir "NetPulseOptimizer.wxs") -d "ProductVersion=$ProductVersion" -d "PublishDir=$publishDir" -o $msiPath
if ($LASTEXITCODE -ne 0) { throw "wix build failed." }

if ($SigningThumbprint) {
    Write-Host "==> Signing the MSI"
    & (Join-Path $installerDir "sign.ps1") -Thumbprint $SigningThumbprint -Path $msiPath
}

Write-Host "==> Done: $msiPath"
