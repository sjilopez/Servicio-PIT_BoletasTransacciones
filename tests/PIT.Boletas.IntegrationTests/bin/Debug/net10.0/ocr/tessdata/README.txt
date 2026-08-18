Este directorio se empaqueta con el servicio Windows.

Requisitos de OCR local:
1. Colocar aqui el archivo spa.traineddata de Tesseract.
2. La ruta por defecto en appsettings es: ocr/tessdata
3. Al arrancar, el servicio valida que exista spa.traineddata.

Nota: durante el despliegue MSI, este archivo debe incluirse para cumplir OCR local sin instalacion manual.
