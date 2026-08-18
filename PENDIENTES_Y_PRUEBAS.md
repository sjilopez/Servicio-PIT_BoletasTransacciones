# PIT_BoletasTransacciones - Pendientes y Pruebas

## Pendientes para continuar manana

1. Endurecimiento de compresion PDF
- Evaluar reemplazo de PdfSharpCore para eliminar advertencias de seguridad transitive de ImageSharp.
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

## Pruebas que se pueden ejecutar desde ya

## A. Pruebas automatizadas
1. Ejecutar:
   - `dotnet test PIT.BoletasTransacciones.slnx`
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
4. Confirmar TXT OCR en `C:\Scans\OCR`.

## C. Pruebas de resiliencia
1. Apagar temporalmente MySQL y verificar `5_DB_PENDING`.
2. Bloquear OCR API y verificar `4_ERROR_OCR` + reintento.
3. Bloquear Azure y verificar permanencia en `7_COPY_AZURE_FILES` o `8_COPY_AZURE_BLOB`.

## Notas
- El proyecto ya compila y los tests actuales pasan.
- Las advertencias NU1902/NU1903 no bloquean compilacion, pero se recomienda resolverlas antes de produccion.
