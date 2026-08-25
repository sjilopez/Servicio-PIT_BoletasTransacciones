param(
    [string]$TargetPath = "d:\wamp\www\Servicio PIT_BoletasTransacciones\src\PIT.Boletas.Worker\ocr\tessdata",
    [switch]$Force = $false
)

$ErrorActionPreference = "Stop"

New-Item -ItemType Directory -Force -Path $TargetPath | Out-Null

$trainedDataFile = Join-Path $TargetPath "spa.traineddata"
if ((Test-Path $trainedDataFile) -and (-not $Force)) {
    Write-Host "spa.traineddata ya existe en $TargetPath. Usa -Force para sobrescribir."
    exit 0
}

$url = "https://github.com/tesseract-ocr/tessdata_best/raw/main/spa.traineddata"
Write-Host "Descargando modelo de alta precision (tessdata_best) spa.traineddata desde $url"
Invoke-WebRequest -Uri $url -OutFile $trainedDataFile

if (-not (Test-Path $trainedDataFile)) {
    throw "No se pudo descargar spa.traineddata"
}

$fileSize = (Get-Item $trainedDataFile).Length / 1MB
$msg = "Archivo OCR listo: $trainedDataFile ({0:N2} MB)" -f $fileSize
Write-Host $msg
