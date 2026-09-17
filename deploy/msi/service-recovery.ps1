$serviceName = "PIT_BoletasTransacciones_v3.0"
$serviceDescription = "Servicio de Digitalizacion OCR de Boletas de Transacciones. Hecho por PIT de Coosajo, R.L. Version: 3.0"

sc.exe config $serviceName start= auto
sc.exe description $serviceName "$serviceDescription"
sc.exe failure $serviceName reset= 0 actions= restart/60000/restart/300000/restart/600000
sc.exe failureflag $serviceName 1
