# Creates signing material only in the current user's certificate store.
# The private key is non-exportable; this script does not change certificate trust.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$subject = 'CN=DM10cn'
$friendlyName = 'PyDeck Preview Signing'
$certificate = Get-ChildItem Cert:\CurrentUser\My -CodeSigningCert | Where-Object {
    $_.Subject -eq $subject -and $_.FriendlyName -eq $friendlyName -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(30)
} | Sort-Object NotAfter -Descending | Select-Object -First 1
if (!$certificate) {
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $subject -FriendlyName $friendlyName `
        -CertStoreLocation 'Cert:\CurrentUser\My' -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 `
        -KeyUsage DigitalSignature -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddYears(2) `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
}
Write-Output $certificate.Thumbprint
