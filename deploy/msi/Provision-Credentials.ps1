param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceExecutable
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

Write-Host "Se registraran secretos en Credential Manager con persistencia local de maquina."
Write-Host "Ejecute este script en cada host antes de iniciar el servicio."

& $ServiceExecutable --provision-credentials
if ($LASTEXITCODE -ne 0) {
    throw "La provision de credenciales fallo con codigo $LASTEXITCODE."
}

Write-Host "Provision completada. El archivo appsettings.local.json ya no necesita secretos."