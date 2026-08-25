param(
    [Parameter(Mandatory = $true)]
    [string]$ServiceExecutable,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $ServiceExecutable)) {
    throw "No se encontro el ejecutable del servicio: $ServiceExecutable"
}

& $ServiceExecutable --create-provisioning-file $OutputFile
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo crear el archivo cifrado. Codigo: $LASTEXITCODE"
}