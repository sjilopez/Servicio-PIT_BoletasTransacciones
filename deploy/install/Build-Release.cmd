@echo off
setlocal
set "ROOT=%~dp0..\.."
set "OUTPUT=%ROOT%\artifacts\publish\win-x64"
set "PADDLE_MODELS=%ROOT%\artifacts\paddle-spike-win-x64\ocr\paddle"

if not exist "%ROOT%\artifacts" mkdir "%ROOT%\artifacts"

if exist "%OUTPUT%" rmdir /S /Q "%OUTPUT%"

dotnet publish "%ROOT%\src\PIT.Boletas.Worker\PIT.Boletas.Worker.csproj" ^
  --configuration Release ^
  --runtime win-x64 ^
  --self-contained true ^
  --output "%OUTPUT%"

if errorlevel 1 (
  echo Error al publicar el servicio.
  exit /b 1
)

copy /Y "%~dp0Install-Service.cmd" "%OUTPUT%\Install-Service.cmd" >nul
copy /Y "%~dp0Uninstall-Service.cmd" "%OUTPUT%\Uninstall-Service.cmd" >nul

if not exist "%PADDLE_MODELS%\det\inference.pdiparams" (
  echo No se encontraron los modelos PaddleOCR en:
  echo %PADDLE_MODELS%
  exit /b 1
)

if not exist "%PADDLE_MODELS%\cls\inference.pdiparams" exit /b 1
if not exist "%PADDLE_MODELS%\rec\inference.pdiparams" exit /b 1
if not exist "%PADDLE_MODELS%\ppocr_keys.txt" exit /b 1

if not exist "%OUTPUT%\ocr\paddle" mkdir "%OUTPUT%\ocr\paddle"
xcopy "%PADDLE_MODELS%\*" "%OUTPUT%\ocr\paddle\" /E /I /Y >nul
if errorlevel 1 (
  echo No se pudieron copiar los modelos PaddleOCR.
  exit /b 1
)

echo Publicacion lista en:
echo %OUTPUT%
endlocal
