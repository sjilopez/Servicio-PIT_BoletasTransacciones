@echo off
setlocal
set "ROOT=%~dp0..\.."
set "OUTPUT=%ROOT%\artifacts\publish\win-x64"

if not exist "%ROOT%\artifacts" mkdir "%ROOT%\artifacts"

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

echo Publicacion lista en:
echo %OUTPUT%
endlocal
