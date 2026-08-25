@echo off
setlocal
set "SERVICE_NAME=PIT_BoletasTransacciones"
set "SERVICE_DESCRIPTION=Servicio de Digitalizacion de Boletas de Transacciones"
set "SERVICE_EXE=%~dp0PIT.Boletas.Worker.exe"
set "ENCRYPTED_FILE=%~1"

net session >nul 2>&1
if errorlevel 1 (
  echo Este instalador debe ejecutarse como Administrador.
  exit /b 1
)

if not exist "%SERVICE_EXE%" (
  echo No se encontro %SERVICE_EXE%.
  exit /b 1
)

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if not errorlevel 1 (
  echo El servicio ya existe. Detengalo y desinstalelo antes de continuar.
  exit /b 1
)

sc.exe create "%SERVICE_NAME%" binPath= "\"%SERVICE_EXE%\"" start= auto DisplayName= "%SERVICE_NAME%" obj= LocalSystem
if errorlevel 1 exit /b 1

sc.exe description "%SERVICE_NAME%" "%SERVICE_DESCRIPTION%" >nul
sc.exe failure "%SERVICE_NAME%" reset= 0 actions= restart/60000/restart/300000/restart/600000 >nul
sc.exe failureflag "%SERVICE_NAME%" 1 >nul

if defined ENCRYPTED_FILE (
  if not exist "%ENCRYPTED_FILE%" (
    echo No se encontro el archivo cifrado: %ENCRYPTED_FILE%
    sc.exe delete "%SERVICE_NAME%" >nul
    exit /b 1
  )

  "%SERVICE_EXE%" --provision-encrypted "%ENCRYPTED_FILE%"
  if errorlevel 1 (
    echo No se pudieron provisionar las credenciales.
    sc.exe delete "%SERVICE_NAME%" >nul
    exit /b 1
  )

  del /Q "%ENCRYPTED_FILE%" >nul 2>&1
)

sc.exe start "%SERVICE_NAME%"
if errorlevel 1 exit /b 1

echo Servicio instalado e iniciado correctamente.
endlocal
