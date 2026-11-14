[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ManifestPath,
    [Parameter(Mandatory)] [string] $PublicKey
)

$ErrorActionPreference = 'Stop'
# The signing secret is independent of distribution-storage credentials and is
# never embedded in the executable or passed on the command line.
if ([string]::IsNullOrWhiteSpace($env:LAUNCHER_UPDATE_PRIVATE_KEY)) {
    throw 'LAUNCHER_UPDATE_PRIVATE_KEY (base64 PKCS#8) is required.'
}
$rsa = [System.Security.Cryptography.RSA]::Create()
try {
    $keyBytes = [Convert]::FromBase64String($env:LAUNCHER_UPDATE_PRIVATE_KEY)
    $bytesRead = 0
    $rsa.ImportPkcs8PrivateKey($keyBytes, [ref] $bytesRead)
    if ($bytesRead -ne $keyBytes.Length -or $rsa.KeySize -lt 3072 -or $rsa.KeySize -gt 8192) {
        throw 'A complete RSA private key of 3072 to 8192 bits is required.'
    }
    if ([Convert]::ToBase64String($rsa.ExportSubjectPublicKeyInfo()) -cne $PublicKey) {
        throw 'Signing key does not match the public key pinned in the launcher build.'
    }
    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ManifestPath))
    if ($bytes.Length -gt 65536) { throw 'Launcher manifest exceeds 64 KiB.' }
    $signature = $rsa.SignData($bytes, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.RSASignaturePadding]::Pss)
    [System.IO.File]::WriteAllBytes("$ManifestPath.sig", $signature)
}
finally {
    if ($null -ne $keyBytes) { [Array]::Clear($keyBytes, 0, $keyBytes.Length) }
    $rsa.Dispose()
}