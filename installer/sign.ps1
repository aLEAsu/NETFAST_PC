<#
.SYNOPSIS
    Signs one file (.exe or .msi) with a certificate already present in
    Cert:\CurrentUser\My, using PowerShell's built-in Authenticode cmdlets - no signtool.exe
    or Windows SDK installation required.

.PARAMETER Thumbprint
    Thumbprint of the signing certificate in Cert:\CurrentUser\My.

.PARAMETER Path
    File to sign.

.PARAMETER TimestampServer
    Optional RFC 3161 timestamp server URL. Real distribution builds should always pass one
    (so the signature stays valid after the certificate itself expires); omitted here by
    default because this project's self-signed test certificate has no meaningful validity
    period to preserve and a real build will supply the CA's own recommended timestamp URL.

.NOTES
    A self-signed certificate (see README.md in this folder) will always show as
    "UnknownError" / "terminates in a root certificate that is not trusted" when checked with
    Get-AuthenticodeSignature - that is the EXPECTED, correct result for a test certificate
    that was deliberately not added to the Trusted Root store. It still proves the file is
    signed and unmodified since signing; it does not and cannot make Windows trust the
    publisher. Only a certificate from a CA Windows already trusts (or an organization's own
    deployed root, for internal distribution) produces a "Valid" trust result.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Thumbprint,

    [Parameter(Mandatory = $true)]
    [string]$Path,

    [string]$TimestampServer = ""
)

$ErrorActionPreference = "Stop"

$cert = Get-ChildItem "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
if (-not $cert) {
    throw "No certificate with thumbprint $Thumbprint found in Cert:\CurrentUser\My."
}

$params = @{
    FilePath      = $Path
    Certificate   = $cert
    HashAlgorithm = "SHA256"
}
if ($TimestampServer) { $params.TimestampServer = $TimestampServer }

$result = Set-AuthenticodeSignature @params
Write-Host ("Signed {0}: {1}" -f $Path, $result.Status)

if ($result.Status -eq "HashMismatch" -or $result.Status -eq "NotSigned") {
    throw "Signing $Path failed: $($result.StatusMessage)"
}
