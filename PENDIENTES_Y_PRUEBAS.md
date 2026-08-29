# PIT_BoletasTransacciones_v2.00 - Pendientes y Pruebas

## Estado y pendientes de produccion

1. Endurecimiento de compresion PDF
- PDFsharp 6.2.4 ya reemplazo PdfSharpCore y el escaneo transitive de vulnerabilidades esta limpio.
- Validar calidad visual y tamano final por tipo de boleta.

2. Cierre de alertas en entorno real
- Configurar Teams Webhook real.
- Configurar SMTP real y credenciales.
- Probar disparo inmediato para eventos `error` y `critical`.

3. Operacion MySQL en entorno real
- Confirmar `MySql:ConnectionString` de produccion.
- Validar tablas operativas creadas automaticamente:
  - `error_event`
  - `duplicate_event`
  - `service_heartbeat`
  - `alert_dispatch`
  - `ocr_result_log`

4. Empaquetado OCR local final
- Incluir `spa.traineddata` en instalador MSI sin paso manual.
- Ejecutar validacion de arranque en equipo limpio.

5. Pruebas E2E por agencia
- Probar flujo completo 1_IN -> 9_LOCAL_BACKUP.
- Probar reintentos con caidas simuladas (OCR API, MySQL, Azure Files, Blob).
- Verificar SLA end-to-end P99 <= 10 minutos.

6. Seguridad de secretos
- Definir la cuenta Windows con la que correra el servicio.
- El servicio corre como `LocalSystem` y lee secretos desde Credential Manager con persistencia local de maquina.
- La primera ejecucion migra automaticamente el archivo legado `C:\Scans\Tools\settings.local.json`.
- IT puede ejecutar `deploy/msi/Provision-Credentials.ps1` como Administrador para provisionar o actualizar secretos.
- Eliminar secretos del archivo local y rotarlos si fueron compartidos o respaldados.
- Para mas de 150 hosts, distribuir el proceso con GPO, Intune, SCCM o la herramienta corporativa conectada a un vault.

7. Resiliencia implementada
- El orquestador captura errores por etapa y continua con las siguientes etapas y ciclos.
- La metadata se escribe atomically y el movimiento PDF/sidecar se revierte si falla.
- Los uploads Azure reutilizan una copia existente cuando el tamano coincide.
- El JSON OCR se respalda en MySQL con SHA-256 e insercion idempotente.
- Los secretos de MySQL, Azure, OCR y alertas se leen desde Windows Credential Manager.

## Pruebas que se pueden ejecutar desde ya

## A. Pruebas automatizadas
1. Ejecutar:
   - `dotnet test tests/PIT.Boletas.UnitTests/PIT.Boletas.UnitTests.csproj`
2. Resultado esperado:
   - Compilacion exitosa.
   - Tests unitarios e integracion en verde.

## B. Smoke test local de pipeline
Prerequisitos:
1. Carpetas base creadas por bootstrap (C:\Scans\...).
2. Archivo OCR de idioma:
   - `src/PIT.Boletas.Worker/ocr/tessdata/spa.traineddata`
3. Configuracion local en:
   - `C:\Scans\Tools\settings.local.json`

Pasos:
1. Arrancar servicio/app en modo consola.
2. Copiar PDF de prueba en `C:\Scans\1_IN`.
3. Verificar movimiento por etapas.
4. Confirmar la fila correspondiente en `ocr_result_log` cuando MySQL este disponible.
5. Confirmar que `10_LOCAL_BACKUP` conserve el PDF sin `.txt` ni `.meta.json`.

## C. Pruebas de resiliencia
1. Apagar temporalmente MySQL y verificar `5_DB_PENDING`.
2. Bloquear OCR API y verificar `4_ERROR_OCR` + reintento.
3. Bloquear Azure y verificar permanencia en `7_COPY_AZURE_FILES` o `8_COPY_AZURE_BLOB`.

## Notas
- El proyecto ya compila y los tests actuales pasan.
- El archivo operativo unico es `C:\ProgramData\PIT-BoletasTransaccionales\Config\appsettings.local.json`.
- La capa de texto no se conserva al reconstruir PDFs rasterizados; debe validarse si la busqueda textual es requisito.
