# Monitor LG

Ícono en la bandeja de Windows que apaga y prende un monitor LG cortándole la señal. Probado con mouse y teclado
simulados por software: no lo despertaron.

## Uso

* Clic izquierdo en el ícono o `Ctrl+Alt+F10`: apaga o prende.
* Clic derecho > *Salir*: prende el monitor y cierra el programa.
* Por comando: `MonitorLG.exe apagar | encender | alternar | salir`.

## Instalación

Requisitos: Windows x64 con .NET Framework 4.x y el driver Parsec VDD (probado con 0.45).

Ejecutá `dist\MonitorLGSetup.exe`. Instala para el usuario actual, sin admin, en `%LOCALAPPDATA%\Programs\MonitorLG`,
con acceso en el menú Inicio e inicio con Windows opcional. Se desinstala desde Configuración > Aplicaciones.

Sin ventana: `MonitorLGSetup.exe /S`, y `/sin-inicio` para que no arranque con Windows.

## Compilar

`build.cmd`. Usa el `csc.exe` incluido en Windows, sin dependencias.

## Cómo funciona

No usa DDC/CI para apagar: en el LG ULTRAGEAR probado (GSM5BB2, DisplayPort), los valores `04` y `05` del código `D6`
se enviaron sin error y el monitor siguió informando "encendido".

En su lugar crea una pantalla virtual con Parsec VDD, pasa el escritorio ahí y desactiva la salida del LG. La
configuración no se guarda en Windows. Si el programa se cierra de golpe, el driver retira la pantalla virtual y
Windows vuelve al LG.

## Limitaciones

* No es un apagado como el del botón: el monitor queda sin señal.
* Considera LG a todo monitor con código de fabricante `GSM`, y los apaga a todos.
* No probado: mouse y teclado físicos, el clic en el ícono, el inicio con Windows tras reiniciar, y suspender o
  cerrar sesión con el monitor apagado.
* El log queda en `%TEMP%\MonitorLG.log`.
