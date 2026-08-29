# Guia de provisioning y operacion

Servicio: `PIT_BoletasTransacciones_v2.00`

Esta guia explica como instalar el servicio, registrar sus credenciales de forma segura y actualizar valores posteriormente.

> Nunca escriba contrasenas, API keys ni connection strings reales dentro de este archivo ni dentro del repositorio.

## 1. Conceptos importantes

El servicio corre como `LocalSystem` y utiliza:

- MySQL con usuario y contrasena.
- Azure Files mediante connection string.
- Azure Blob mediante connection string.
- OCR externo mediante API key.
- Credential Manager de Windows para guardar secretos.

La configuracion no sensible queda en:

```text
C:\ProgramData\PIT-BoletasTransaccionales\Config\appsettings.local.json
```

El servicio ya no utiliza ni crea:

```text
C:\Scans\Tools\settings.local.json
```

En equipos antiguos, el servicio puede leer ese archivo una sola vez para migrarlo durante la actualizacion. Despues de confirmar la migracion, `C:\Scans\Tools` puede eliminarse.

En una primera ejecucion, si existe el archivo antiguo, el servicio intenta migrar sus valores al nuevo esquema. Los secretos se guardan en Credential Manager y el archivo antiguo se elimina despues de completar la migracion.

## 2. Requisitos para IT

- Windows 10/11 o Windows Server compatible.
- Permisos de Administrador local.
- Ejecutable publicado del servicio.
- Acceso a las credenciales vigentes de MySQL, Azure y OCR.
- El servicio debe instalarse con la cuenta `LocalSystem`.
- El archivo cifrado debe transportarse por un canal protegido y eliminarse despues de usarlo.
- La V2 utiliza PaddleOCR local en modo CPU; no requiere Python ni descargas durante la ejecucion.
- El paquete de instalacion debe incluir las DLL nativas Paddle y la carpeta `ocr\paddle` con los modelos.

## 3. Crear el archivo cifrado

Esta operacion se realiza en un equipo seguro. Use el ejecutable publicado; el ejemplo con `bin\Debug` es solamente para pruebas locales.

```powershell
.\PIT.BoletasTransacciones.exe `
  --create-provisioning-file `
  "C:\PIT-Seguro\pit-credenciales.enc.json"
```

El programa solicitara los siguientes valores:

1. `MySql:ConnectionString`
2. `AzureFiles:ConnectionString`
3. `AzureBlob:ConnectionString`
4. `ExternalOcr:ApiKey`
5. `Alerts:TeamsWebhook`
6. `Alerts:SmtpUser`
7. `Alerts:SmtpPassword`
8. Contrasena del archivo cifrado
9. Confirmacion de la contrasena del archivo cifrado

Los valores introducidos como secretos no se muestran en pantalla.

La contrasena del archivo cifrado no se guarda en el archivo. Si se pierde, hay que crear un archivo nuevo.

## 4. Instalar y provisionar un host

1. Instale el servicio como `LocalSystem`.
2. Copie temporalmente el archivo cifrado al host destino.
3. Abra PowerShell como Administrador.
4. Ejecute:

```powershell
.\deploy\msi\Provision-EncryptedCredentials.ps1 `
  -ServiceExecutable "C:\Program Files\PIT Boletas Transacciones\PIT.BoletasTransacciones.exe" `
  -EncryptedFile "C:\PIT-Seguro\pit-credenciales.enc.json" `
  -DeleteAfterProvisioning
```

El script:

- Solicita la contrasena del archivo.
- Descifra el contenido en memoria.
- Guarda los secretos en Windows Credential Manager.
- No escribe los secretos en `appsettings.local.json`.
- Elimina el archivo cifrado si se utilizo `-DeleteAfterProvisioning`.

## 5. Verificar las credenciales

Ejecute el diagnostico como Administrador o con la misma identidad que utilizara el servicio:

```powershell
& "C:\Program Files\PIT Boletas Transacciones\PIT.BoletasTransacciones.exe" `
  --check-credentials
```

El resultado esperado es parecido a:

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

El comando solo muestra nombres y nunca imprime los valores.

Despues inicie o reinicie el servicio:

```powershell
Start-Service PIT_BoletasTransacciones_v2.00
# o, si ya estaba iniciado:
Restart-Service PIT_BoletasTransacciones_v2.00
```

### Instalacion sin PowerShell

Los hosts remotos no necesitan PowerShell. Copie el contenido de `artifacts\publish\win-x64` y el archivo cifrado al host destino. Abra una consola `cmd.exe` como Administrador y ejecute:

```cmd
Install-Service.cmd
```

Si `pit-credenciales.enc.json` se encuentra en la misma carpeta que `Install-Service.cmd`, el instalador lo detecta automaticamente, lo utiliza y lo elimina al finalizar. Tambien se puede indicar una ruta alternativa:

```cmd
Install-Service.cmd "C:\PIT-Seguro\pit-credenciales.enc.json"
```

El comando crea el servicio como `LocalSystem`, provisiona el archivo cifrado, elimina el archivo temporal y arranca el servicio. Para instalar sin cambiar credenciales, ejecute `Install-Service.cmd` sin argumento.

Antes de copiar el paquete, generelo desde el repositorio V2:

```cmd
deploy\install\Build-Release.cmd
```

El script publica para `win-x64`, incluye las dependencias nativas de PaddleOCR y copia los modelos desde `artifacts\paddle-spike-win-x64\ocr\paddle`. Si faltan los modelos, la publicacion se detiene.

En cada host destino:

1. Detenga la version anterior si existe: `sc stop PIT_BoletasTransacciones_v2.00`.
2. Copie todo el contenido de `artifacts\publish\win-x64` a `C:\Program Files\PIT Boletas Transacciones`.
3. Copie el archivo cifrado junto a `Install-Service.cmd` o indique su ruta como primer argumento.
4. Ejecute `Install-Service.cmd` como Administrador.
5. Verifique las credenciales y el servicio con los comandos de esta guia.

Confirme que el paquete contiene estos recursos antes de distribuirlo:

```text
PaddleOCR.dll
paddle_inference.dll
opencv_world470.dll
ocr\paddle\det\inference.pdiparams
ocr\paddle\cls\inference.pdiparams
ocr\paddle\rec\inference.pdiparams
ocr\paddle\ppocr_keys.txt
```

Para desinstalar:

```cmd
Uninstall-Service.cmd
```

La desinstalacion no borra documentos ni credenciales de Credential Manager.

## 6. Cambiar la API key del OCR

Cuando el proveedor entregue una API key nueva:

1. Cree un nuevo archivo cifrado.
2. En `ExternalOcr:ApiKey`, introduzca la clave nueva.
3. En los demas campos presione Enter para conservarlos fuera del nuevo paquete.
4. Provisione el archivo en cada host.
5. Reinicie el servicio.

Creacion del paquete:

```powershell
.\PIT.BoletasTransacciones.exe `
  --create-provisioning-file `
  "C:\PIT-Seguro\actualizar-ocr.enc.json"
```

Provision:

```powershell
.\deploy\msi\Provision-EncryptedCredentials.ps1 `
  -ServiceExecutable "C:\Program Files\PIT Boletas Transacciones\PIT.BoletasTransacciones.exe" `
  -EncryptedFile "C:\PIT-Seguro\actualizar-ocr.enc.json" `
  -DeleteAfterProvisioning
```

Reinicio:

```powershell
Restart-Service PIT_BoletasTransacciones_v2.00
```

## 7. Cambiar MySQL o Azure

El procedimiento es el mismo que para la API key:

- Para cambiar MySQL, complete `MySql:ConnectionString` y deje los otros campos vacios.
- Para cambiar Azure Files, complete `AzureFiles:ConnectionString`.
- Para cambiar Azure Blob, complete `AzureBlob:ConnectionString`.

No coloque estos valores directamente en el MSI, en la linea de comandos ni en el repositorio.

## 8. Despliegue remoto

El archivo cifrado puede distribuirse usando una herramienta corporativa como:

- Intune.
- SCCM.
- GPO.
- PowerShell Remoting.
- Herramienta de administracion de endpoints.

La herramienta debe obtener el archivo desde una ubicacion protegida, ejecutar el provisioning con permisos elevados y eliminar el archivo despues.

No incluya la contrasena del archivo cifrado dentro de una GPO, script publico, repositorio o parametro visible. Para despliegues masivos, IT debe obtenerla desde un vault corporativo o mecanismo seguro.

## 9. Rotacion y seguridad

- Rote las credenciales si el archivo antiguo fue compartido fuera del equipo.
- No envie secretos por correo ni por chat.
- Limite los permisos de las connection strings.
- Use una cuenta MySQL con los permisos estrictamente necesarios.
- No registre secretos en logs.
- No respalde el archivo cifrado junto con su contrasena.
- Al formatear un equipo, Credential Manager se pierde y hay que provisionar nuevamente.
- Al cambiar la API key, no es necesario modificar el codigo.

## 10. Configuracion no sensible

La configuracion operativa queda en:

```text
C:\ProgramData\PIT-BoletasTransaccionales\Config\appsettings.local.json
```

Ese archivo puede contener valores no sensibles como:

- Intervalo de polling.
- Retencion.
- Parametros OCR.
- Parametros de compresion.
- Calidad JPEG configurada actualmente en `50`.
- Nombres de contenedor o share.
- Umbrales de validacion.

No agregue passwords, API keys ni connection strings a ese archivo.

## 11. Pruebas locales disponibles

Crear la publicacion instalable sin PowerShell en el host destino:

```cmd
deploy\install\Build-Release.cmd
```

El resultado queda en `artifacts\publish\win-x64`.

Diagnostico de Credential Manager:

```powershell
.\src\PIT.Boletas.Worker\bin\Debug\net10.0\PIT.BoletasTransacciones.exe --check-credentials
```

Compilacion:

```powershell
dotnet build .\src\PIT.Boletas.Worker\PIT.Boletas.Worker.csproj --no-restore
```

Pruebas unitarias:

```powershell
dotnet test .\tests\PIT.Boletas.UnitTests\PIT.Boletas.UnitTests.csproj --no-restore
```

Pruebas de resiliencia incluidas:

- Escrituras concurrentes de metadata.
- Movimiento conjunto de PDF y sidecar.
- Continuidad del worker ante errores de etapas.
- Reintentos idempotentes de Azure.
- Insercion idempotente del JSON OCR en MySQL.

## 12. Solucion de problemas

### Credenciales configuradas: 0

- Ejecute el provisioning como Administrador.
- Confirme que uso el ejecutable correcto.
- Confirme que el servicio y la prueba utilizan el mismo equipo.
- Vuelva a ejecutar `--check-credentials`.

### No se puede descifrar el archivo

- Verifique la contrasena.
- Confirme que el archivo no fue modificado.
- Cree un archivo cifrado nuevo si la contrasena se perdio.

### El servicio no conecta con MySQL, Azure u OCR

- Verifique la API key o connection string vigente.
- Revise que el host tenga salida de red.
- Revise los logs del servicio.
- Ejecute nuevamente el provisioning y reinicie el servicio.

### El servicio no inicia despues de formatear el equipo

Credential Manager se borra al formatear. Ejecute nuevamente el provisioning en el equipo nuevo.

## 13. Archivos relacionados

- `deploy/msi/New-EncryptedProvisioningFile.ps1`
- `deploy/msi/Provision-EncryptedCredentials.ps1`
- `deploy/msi/Provision-Credentials.ps1`
- `src/PIT.Boletas.Infrastructure/Security/WindowsCredentialStore.cs`
- `src/PIT.Boletas.Worker/Program.cs`
- `PENDIENTES_Y_PRUEBAS.md`
