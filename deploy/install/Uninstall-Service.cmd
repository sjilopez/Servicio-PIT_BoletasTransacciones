@echo off
setlocal
set "SERVICE_NAME=PIT_BoletasTransacciones_v2.00"

net session >nul 2>&1
if errorlevel 1 (
  echo Este desinstalador debe ejecutarse como Administrador.
  exit /b 1
)

sc.exe stop "%SERVICE_NAME%" >nul 2>&1
sc.exe delete "%SERVICE_NAME%"
if errorlevel 1 (
  echo No se pudo eliminar el servicio.
  exit /b 1
)

echo Servicio eliminado. Las carpetas de documentos y Credential Manager no se borraron.
endlocal
