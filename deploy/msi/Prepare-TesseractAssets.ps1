param(
    [string]$TargetPath = "d:\wamp\www\Servicio PIT_BoletasTransacciones\src\PIT.Boletas.Worker\ocr\tessdata"
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $TargetPath | Out-Null

$trainedDataFile = Join-Path $TargetPath "spa.traineddata"
if (Test-Path $trainedDataFile) {
    Write-Host "spa.traineddata ya existe en $TargetPath"
    exit 0
}

$url = "https://github.com/tesseract-ocr/tessdata/raw/main/spa.traineddata"
Write-Host "Descargando spa.traineddata desde $url"
Invoke-WebRequest -Uri $url -OutFile $trainedDataFile

if (-not (Test-Path $trainedDataFile)) {
    throw "No se pudo descargar spa.traineddata"
}

Write-Host "Archivo OCR listo: $trainedDataFile"
