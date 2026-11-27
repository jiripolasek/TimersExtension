[CmdletBinding()]
param([Parameter(Mandatory)][string] $Path)

$ErrorActionPreference = 'Stop'
$signature = Get-AuthenticodeSignature -LiteralPath $Path
$signer = $signature.SignerCertificate
if ($signature.Status -ne 'Valid' -or -not $signer) {
    throw "MSIX signature verification failed for '$Path': $($signature.StatusMessage)"
}

$storeSigningOid = '1.3.6.1.4.1.311.76.3.1'
$signerUsages = $signer.Extensions |
    Where-Object { $_ -is [Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension] } |
    ForEach-Object { $_.EnhancedKeyUsages.Value }
if ($storeSigningOid -notin $signerUsages) {
    throw "MSIX '$Path' must be signed by the Microsoft Store."
}

$archive = [IO.Compression.ZipFile]::OpenRead([IO.Path]::GetFullPath($Path))
$data = [IO.MemoryStream]::new()
try {
    $entry = $archive.GetEntry('AppxSignature.p7x')
    if ($null -eq $entry) { throw "MSIX '$Path' has no embedded package signature." }
    $stream = $entry.Open()
    try { $stream.CopyTo($data) }
    finally { $stream.Dispose() }
    $encoded = $data.ToArray()
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    # The signed CMS envelope follows the four-byte PKCX header.
    $cms.Decode($encoded[4..($encoded.Length - 1)])
}
finally {
    $data.Dispose()
    $archive.Dispose()
}

# Microsoft's corporate signing roots from https://www.microsoft.com/pkiops/docs/repository.htm.
$microsoftRoots = @(
    '3B1EFD3A66EA28B16697394703A72CA340A05BD5'
    '8F43288AD272F3103B6FB1428485EA3014C0BCFE'
    '06F1AA330B927B753A40E68CDF22E34BCBEF3352'
)
$chain = [Security.Cryptography.X509Certificates.X509Chain]::new()
try {
    # Authenticode already validated the signature and timestamp. Check the signing chain's origin.
    $chain.ChainPolicy.VerificationFlags = 'IgnoreNotTimeValid'
    $chain.ChainPolicy.RevocationMode = 'NoCheck'
    $chain.ChainPolicy.ExtraStore.AddRange($cms.Certificates)
    [void] $chain.ChainPolicy.ApplicationPolicy.Add([Security.Cryptography.Oid]::new($storeSigningOid))
    if (-not $chain.Build($signer) -or $chain.ChainElements[-1].Certificate.Thumbprint -notin $microsoftRoots) {
        throw "MSIX '$Path' must chain to a trusted Microsoft Store signing authority."
    }
}
finally { $chain.Dispose() }
