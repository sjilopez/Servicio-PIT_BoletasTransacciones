# Guia de instalacion y pruebas

## PIT Boletas Transacciones V2

Servicio: `PIT_BoletasTransacciones_v2.00`

Version Git: `v2.0.0`

Rama de trabajo: `feature/paddleocr-pipeline-v2`

Esta guia describe como generar, instalar, configurar y probar la V2 con PaddleOCR local.

> No guardar contrasenas, API keys ni connection strings en este archivo ni en Git.

## 1. Requisitos

- Windows 10/11 o Windows Server compatible.
- Windows x64.
- Permisos de Administrador local.
- .NET SDK 10 solamente en el equipo donde se compile.
- Acceso a MySQL, Azure Files, Azure Blob y OCR externo, segun corresponda.
- El servicio se ejecuta como `LocalSystem`.
- PaddleOCR funciona localmente en CPU; no requiere Python ni Internet durante la ejecucion.
- Las credenciales se guardan tambien en un archivo cifrado con DPAPI `LocalMachine`, accesible para `LocalSystem` y Administradores.

## 2. Generar el paquete V2

Ejecutar desde la raiz del repositorio:

```cmd
deploy\install\Build-Release.cmd
```

El paquete queda en:

```text
artifacts\publish\win-x64
```

El script publica el Worker autocontenido para Windows x64 e incluye:

- Ejecutable `PIT.BoletasTransacciones.exe`.
- DLL nativas de PaddleOCR.
- Modelos PaddleOCR.
- Recursos Tesseract.
- `Install-Service.cmd`.
- `Uninstall-Service.cmd`.

El script necesita encontrar los modelos en:

```text
artifacts\paddle-spike-win-x64\ocr\paddle
```

Si se reconstruye el proyecto desde otro equipo, conservar esa carpeta de modelos o copiarla antes de ejecutar `Build-Release.cmd`.

## 3. Validar el paquete antes de distribuirlo

Confirmar que existen estos archivos:

```text
artifacts\publish\win-x64\PIT.BoletasTransacciones.exe
artifacts\publish\win-x64\PaddleOCR.dll
artifacts\publish\win-x64\paddle_inference.dll
artifacts\publish\win-x64\opencv_world470.dll
artifacts\publish\win-x64\ocr\paddle\det\inference.pdiparams
artifacts\publish\win-x64\ocr\paddle\cls\inference.pdiparams
artifacts\publish\win-x64\ocr\paddle\rec\inference.pdiparams
artifacts\publish\win-x64\ocr\paddle\ppocr_keys.txt
```

## 4. Preparar credenciales

En un equipo seguro, crear el archivo cifrado:

```powershell
.\PIT.BoletasTransacciones.exe `
  --create-provisioning-file `
  "C:\PIT-Seguro\pit-credenciales.enc.json"
```

El programa solicita:

1. `MySql:ConnectionString`
2. `AzureFiles:ConnectionString`
3. `AzureBlob:ConnectionString`
4. `ExternalOcr:ApiKey`
5. `Alerts:TeamsWebhook`
6. `Alerts:SmtpUser`
7. `Alerts:SmtpPassword`
8. Contrasena del archivo cifrado
9. Confirmacion de la contrasena

La contrasena del archivo cifrado no se almacena. Si se pierde, crear otro archivo.

## 5. Instalar en un host nuevo

1. Copiar todo el contenido de `artifacts\publish\win-x64` a:

```text
C:\Program Files\PIT Boletas Transacciones
```

2. Copiar `pit-credenciales.enc.json` junto a `Install-Service.cmd`.

3. Abrir `cmd.exe` como Administrador.

4. Ejecutar:

```cmd
cd /d "C:\Program Files\PIT Boletas Transacciones"
Install-Service.cmd pit-credenciales.enc.json
```

El instalador crea el servicio `PIT_BoletasTransacciones_v2.00` como `LocalSystem`, configura reinicio automatico, registra las credenciales, elimina el archivo cifrado e inicia el servicio.

Durante el provisioning se crea en el host:

```text
C:\ProgramData\PIT-BoletasTransaccionales\Config\machine-secrets.bin
```

Ese archivo queda cifrado y protegido para que lo pueda leer `LocalSystem`. No es necesario que cada usuario de Windows registre las credenciales.

## 6. Verificar la instalacion

Consultar el estado del servicio:

```cmd
sc query PIT_BoletasTransacciones_v2.00
```

Verificar las credenciales sin mostrar sus valores:

```powershell
& "C:\Program Files\PIT Boletas Transacciones\PIT.BoletasTransacciones.exe" `
  --check-credentials
```

Resultado esperado:

```text
Credenciales configuradas: 7
OK MySql:ConnectionString
OK AzureFiles:ConnectionString
OK AzureBlob:ConnectionString
OK ExternalOcr:ApiKey
OK Alerts:TeamsWebhook
OK Alerts:SmtpUser
OK Alerts:SmtpPassword
```

Revisar tambien el Event Viewer de Windows si el servicio no inicia.

## 7. Configuracion por host

La configuracion no sensible queda en:

```text
C:\ProgramData\PIT-BoletasTransaccionales\Config\appsettings.local.json
```

Paddle es el motor predeterminado de la V2 y usa rutas relativas al paquete:

```json
{
  "LocalOcr": {
    "Enabled": true,
    "Engine": "Paddle",
    "PaddleDetModelPath": "ocr/paddle/det",
    "PaddleClsModelPath": "ocr/paddle/cls",
    "PaddleRecModelPath": "ocr/paddle/rec",
    "PaddleDictionaryPath": "ocr/paddle/ppocr_keys.txt"
  }
}
```

No colocar en este archivo:

- Passwords.
- API keys.
- Connection strings.

Esos valores se cargan desde Windows Credential Manager.

El origen principal para el servicio es `machine-secrets.bin`, cifrado con DPAPI de maquina. Credential Manager se conserva como compatibilidad para diagnostico y actualizaciones anteriores.

## 8. Probar PaddleOCR directamente

Con un PDF disponible, ejecutar:

```powershell
& "C:\Program Files\PIT Boletas Transacciones\PIT.BoletasTransacciones.exe" `
  --check-ocr "C:\Scans\Irving.pdf" `
  --show-ocr-text
```

Resultado esperado:

```text
OCR OK. Caracteres: ...
----- INICIO TEXTO OCR -----
...
----- FIN TEXTO OCR -----
```

En una boleta transaccional debe aparecer texto similar a:

```text
BOLETA DE TRANSACCIONES
```

## 9. Probar el flujo V2 completo

El Worker debe estar iniciado. En una segunda consola, copiar un PDF nuevo:

```powershell
Copy-Item "C:\tmp\Irving.pdf" "C:\Scans\1_IN\Irving-prueba-01.pdf"
```

El Worker espera que el archivo termine de copiarse y luego lo procesa.

Revisar las etapas:

```powershell
Get-ChildItem C:\Scans\1_IN
Get-ChildItem C:\Scans\2_VALIDATE
Get-ChildItem C:\Scans\4_OCR_EXTERNO
Get-ChildItem C:\Scans\5_ERROR_OCR
Get-ChildItem C:\Scans\6_DB_PENDING
Get-ChildItem C:\Scans\7_COPY_AZURE_FILE
Get-ChildItem C:\Scans\8_COMPRESS
Get-ChildItem C:\Scans\9_COPY_AZURE_BLOB
Get-ChildItem C:\Scans\10_LOCAL_BACKUP
```

Para un documento transaccional, el recorrido esperado empieza asi:

```text
1_IN -> 2_VALIDATE -> 4_OCR_EXTERNO
```

La validacion requiere:

- Encabezado `BOLETA DE TRANSACCIONES`.
- Al menos 3 indicadores de la plantilla.

El encabezado puede aparecer como texto OCR y el documento aun asi ser rechazado si no alcanza los indicadores configurados. El `.meta.json` se usa temporalmente durante las etapas del pipeline y se elimina al llegar a `10_LOCAL_BACKUP`.

## 10. Resultado esperado por escenario

### OCR externo correcto

```text
4_OCR_EXTERNO -> persistencia MySQL -> siguientes etapas V2
```

### OCR externo fallido

```text
4_OCR_EXTERNO -> 5_ERROR_OCR
```

### MySQL no disponible

```text
6_DB_PENDING
```

El servicio reintenta MySQL durante el periodo configurado, actualmente 3 minutos por intento de ciclo.

### Documento no transaccional

```text
2_VALIDATE -> 7_COPY_AZURE_FILE
```

### Azure Blob requerido

```text
8_COMPRESS -> 9_COPY_AZURE_BLOB -> 10_LOCAL_BACKUP
```

### Azure Blob no requerido

```text
8_COMPRESS -> 10_LOCAL_BACKUP
```

## 11. Actualizar un host existente

El instalador no reemplaza un servicio que ya existe.

1. Detener el servicio:

```cmd
sc stop PIT_BoletasTransacciones_v2.00
```

2. Ejecutar el desinstalador como Administrador:

```cmd
Uninstall-Service.cmd
```

3. Respaldar, si corresponde, la configuracion de:

```text
C:\ProgramData\PIT-BoletasTransaccionales\Config
```

4. Reemplazar el contenido de:

```text
C:\Program Files\PIT Boletas Transacciones
```

por el nuevo paquete `artifacts\publish\win-x64`.

5. Ejecutar nuevamente:

```cmd
Install-Service.cmd
```

6. Provisionar credenciales solo si fueron renovadas.

Si el host ya tenia una instalacion anterior, vuelve a ejecutar el provisioning despues de actualizar para crear el archivo DPAPI de maquina:

```cmd
PIT.BoletasTransacciones.exe --provision-encrypted "C:\PIT-Seguro\pit-credenciales.enc.json"
```

Ejecuta el comando como Administrador y elimina el archivo cifrado despues de confirmar el mensaje de provisioning correcto.

Los documentos de `C:\Scans` y las credenciales de Credential Manager no se borran al desinstalar.

## 12. Actualizar solo credenciales

Crear un nuevo archivo cifrado y provisionarlo:

```powershell
& "C:\Program Files\PIT Boletas Transacciones\PIT.BoletasTransacciones.exe" `
  --create-provisioning-file "C:\PIT-Seguro\actualizar-credenciales.enc.json"
```

```powershell
.\deploy\msi\Provision-EncryptedCredentials.ps1 `
  -ServiceExecutable "C:\Program Files\PIT Boletas Transacciones\PIT.BoletasTransacciones.exe" `
  -EncryptedFile "C:\PIT-Seguro\actualizar-credenciales.enc.json" `
  -DeleteAfterProvisioning
```

Reiniciar:

```powershell
Restart-Service PIT_BoletasTransacciones_v2.00
```

## 13. Base de datos

Antes de usar MySQL en produccion, ejecutar:

```text
deploy\database\Upgrade-OcrResultLog.sql
```

La V2 tambien intenta crear o actualizar automaticamente las columnas necesarias al conectar.

## 14. Desinstalar

Como Administrador:

```cmd
Uninstall-Service.cmd
```

La desinstalacion no borra:

- Documentos de `C:\Scans`.
- Configuracion en `C:\ProgramData\PIT-BoletasTransaccionales`.
- Credenciales de Windows Credential Manager.

## 15. Pruebas del repositorio

Desde la raiz del repositorio:

```powershell
dotnet test .\tests\PIT.Boletas.UnitTests\PIT.Boletas.UnitTests.csproj --configuration Release
```

Resultado esperado actual:

```text
10 correcto
0 errores
```

Prueba rapida de Paddle durante desarrollo:

```powershell
.\scripts\Test-PaddleOcrLocal.ps1 -PdfPath "C:\tmp\Irving.pdf"
```

## 16. Version Git

Consultar la version:

```cmd
git describe --tags --always
```

La version cerrada es:

```text
v2.0.0
```
