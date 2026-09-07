[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$PdfPath = 'C:\tmp\Irving.pdf',

    [Parameter(Mandatory = $false)]
    [string]$Configuration = 'Release',

    [Parameter(Mandatory = $false)]
    [string]$ModelSource = ''
)

$ErrorActionPreference = 'Stop'
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repositoryRoot 'src\PIT.Boletas.Worker\PIT.Boletas.Worker.csproj'
$publishPath = Join-Path $repositoryRoot 'artifacts\paddle-local-test'
$paddleRoot = Join-Path $publishPath 'ocr\paddle'
$defaultModelSource = Join-Path $repositoryRoot 'artifacts\paddle-spike-win-x64\ocr\paddle'
$executable = Join-Path $publishPath 'PIT.BoletasTransacciones.exe'

if (-not (Test-Path $PdfPath)) {
    throw "No existe el PDF de prueba: $PdfPath"
}

Write-Host 'Compilando servicio para prueba local...'
dotnet restore $project --runtime win-x64
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo restaurar el Worker para win-x64."
}

dotnet build $project --configuration $Configuration --runtime win-x64 --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo compilar el Worker."
}

Write-Host 'Publicando artefacto local de prueba...'
dotnet publish $project --configuration $Configuration --runtime win-x64 --self-contained true --output $publishPath --no-restore
if ($LASTEXITCODE -ne 0) {
    throw "No se pudo publicar el artefacto Paddle para win-x64."
}

if ([string]::IsNullOrWhiteSpace($ModelSource)) {
    $ModelSource = $defaultModelSource
}

if (-not (Test-Path $ModelSource)) {
    throw "No existe la carpeta de modelos PaddleOCR: $ModelSource"
}

New-Item -ItemType Directory -Force -Path $paddleRoot | Out-Null
Copy-Item (Join-Path $ModelSource '*') $paddleRoot -Recurse -Force

$requiredFiles = @(
    (Join-Path $paddleRoot 'det\inference.pdiparams'),
    (Join-Path $paddleRoot 'cls\inference.pdiparams'),
    (Join-Path $paddleRoot 'rec\inference.pdiparams'),
    (Join-Path $paddleRoot 'ppocr_keys.txt')
)

foreach ($requiredFile in $requiredFiles) {
    if (-not (Test-Path $requiredFile)) {
        throw "Falta un recurso PaddleOCR en el artefacto local: $requiredFile"
    }
}

$arguments = @(
    '--check-ocr', $PdfPath,
    '--show-ocr-text',
    '--LocalOcr:Engine=Paddle',
    "--LocalOcr:PaddleDetModelPath=$(Join-Path $paddleRoot 'det')",
    "--LocalOcr:PaddleClsModelPath=$(Join-Path $paddleRoot 'cls')",
    "--LocalOcr:PaddleRecModelPath=$(Join-Path $paddleRoot 'rec')",
    "--LocalOcr:PaddleDictionaryPath=$(Join-Path $paddleRoot 'ppocr_keys.txt')",
    '--LocalOcr:PaddleReadHeader=true',
    '--LocalOcr:PaddleHeaderCropX=0',
    '--LocalOcr:PaddleHeaderCropY=0',
    '--LocalOcr:PaddleHeaderCropRatio=0.50',
    '--LocalOcr:PaddleHeaderPreprocessing=true',
    '--LocalOcr:PaddleHeaderScale=2',
    '--LocalOcr:PaddleHeaderContrast=1.35'
)

Write-Host 'Ejecutando PaddleOCR local...'
Push-Location $publishPath
try {
    $output = & $executable @arguments
    $output | ForEach-Object { Write-Host $_ }
    if ($LASTEXITCODE -ne 0) {
        throw "La prueba OCR fallo con codigo de salida $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$joinedOutput = $output -join [Environment]::NewLine
if ($joinedOutput -match '(?i)BOLETA|TRANSACC') {
    Write-Host 'RESULTADO: encabezado detectado.' -ForegroundColor Green
    exit 0
}

Write-Host 'RESULTADO: OCR funcional, pero encabezado no detectado.' -ForegroundColor Yellow
exit 2
