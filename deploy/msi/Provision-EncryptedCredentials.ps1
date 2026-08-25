param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceExecutable,

    [Parameter(Mandatory = $true)]
    [string]$EncryptedFile,

    [switch]$DeleteAfterProvisioning
)

$ErrorActionPreference = "Stop"

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Este script debe ejecutarse como Administrador."
}

if (-not (Test-Path -LiteralPath $ServiceExecutable)) {
    throw "No se encontro el ejecutable del servicio: $ServiceExecutable"
}

if (-not (Test-Path -LiteralPath $EncryptedFile)) {
    throw "No se encontro el archivo cifrado: $EncryptedFile"
}

& $ServiceExecutable --provision-encrypted $EncryptedFile
if ($LASTEXITCODE -ne 0) {
    throw "La provision cifrada fallo. Codigo: $LASTEXITCODE"
}

if ($DeleteAfterProvisioning) {
    Remove-Item -LiteralPath $EncryptedFile -Force
    Write-Host "Archivo cifrado eliminado despues de la provision."
}