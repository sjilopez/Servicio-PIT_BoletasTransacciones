$serviceName = "PIT_BoletasTransacciones"
$serviceDescription = "Servicio de Digitalizacion de Boletas de Transacciones, donde se contempla el nombre del PDF individual, OCR, Compresion y Resguardo de las boletas. Desarrollado por el area de PIT de Coosajo, R.L."

sc.exe config $serviceName start= auto
sc.exe description $serviceName "$serviceDescription"
sc.exe failure $serviceName reset= 0 actions= restart/60000/restart/300000/restart/600000
sc.exe failureflag $serviceName 1
