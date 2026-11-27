# Capture

Aplicación pequeña de escritorio para Windows que permite **capturar una zona de la pantalla y explicarla en la propia imagen con flechas y texto**. «Captura y anota» es el nombre provisional que aparece en las maquetas. El repositorio conserva las maquetas iniciales y una versión de prueba para Windows.

## Flujo de trabajo

1. **Capturar.** Arrastrar para seleccionar un área de pantalla, ajustar el recorte con los tiradores y confirmar la captura.
2. **Anotar.** Marcar una zona de la captura con un rectángulo, señalar un punto con una flecha y añadir texto explicativo. Seleccionar, mover y cambiar el color de cada anotación por separado. La maqueta propone un control de grosor para la flecha.
3. **Compartir.** La maqueta propone **Copiar** para pegar la captura anotada en otra aplicación y **Guardar** para conservarla como imagen.

## Maquetas iniciales

### Selección del área de captura

La pantalla se oscurece fuera de la selección. El marco azul tiene tiradores para ajustar el recorte antes de capturar.

![Selección de un área de pantalla con un marco y tiradores](docs/maquetas/01-seleccionar-area.png)

### Edición de la captura

La barra superior reúne **Seleccionar**, **Rectángulo**, **Flecha** y **Texto**. El panel lateral muestra el color y el grosor de la flecha seleccionada.

![Editor de captura con un rectángulo, una flecha, textos y selector de color](docs/maquetas/02-anotar-captura.png)

Las imágenes son las maquetas iniciales del flujo y usan contenido de ejemplo.

## Versión de escritorio para Windows

Capture se ejecuta con `Capture.exe` desde una carpeta descomprimida. Usa C# 5, Windows Forms y .NET Framework 4.8, incluido en Windows 11 y en las versiones recientes de Windows 10.

1. **Captura.** Capture arranca junto al reloj. Abre la ventana que quieras recortar y pulsa **Ctrl+Alt+R**, o elige **Nueva captura** en el icono de Capture. Arrastra la cruz blanca con borde oscuro para elegir la zona, ajusta los bordes y confirma con **Capturar** o **Intro**. El editor muestra el recorte al 100 % si cabe y reduce los recortes grandes para que se vean completos. Gira la rueda sobre la captura para ampliarla o reducirla alrededor del puntero. **Ampliar vista** ajusta la imagen al lienzo y **Restablecer vista** vuelve al tamaño inicial. Pulsa **Esc** para cancelar.
2. **Anota.** Con **Flecha**, pulsa donde debe quedar la punta y arrastra para extender la línea. **Línea** dibuja un segmento sin punta. Con **Elipse**, pulsa en el centro y arrastra hacia un lado para dar ancho o hacia arriba o abajo para dar alto. La figura crece simétricamente alrededor del primer clic. Arrastra los extremos de una flecha o línea para redimensionarla. Para cambiar el tamaño de un rectángulo o una elipse ya dibujados, usa sus tiradores. Los ajustes de la selección aparecen en la barra superior. En ventanas estrechas ocupan una fila reservada para mantener estable el tamaño de la captura.
3. **Comparte.** Usa **Copiar** para llevar la imagen al portapapeles o **Guardar PNG** para escribirla en un archivo. Puedes añadir también rectángulos y texto.
4. **Cierra.** La **X** cierra Capture directamente y descarta los cambios pendientes. **Salir** en el área de notificación pide confirmación si hay cambios pendientes. Ambas opciones liberan el atajo al cerrar. Minimizar deja el atajo disponible.

Para actualizar, cierra la versión anterior con **Salir** desde su icono junto al reloj. Si ya tienes el inicio automático o el acceso anclado, guarda una copia del ejecutable anterior fuera de la carpeta y sustituye `Capture.exe` en su ubicación actual por el del ZIP. Así los accesos seguirán apuntando al mismo sitio. Para volver a la entrega anterior, cierra Capture y restaura la copia en esa misma ubicación. Tus imágenes PNG permanecen donde las guardaste.

Si Windows mantiene bloqueadas las carpetas `Capture-atajo-y-linea` o `Capture-recorte-y-trazos`, guarda las anotaciones abiertas y ejecuta `Liberar-carpetas-Capture.cmd` incluido en la entrega. El comando cierra los procesos de esas dos versiones en tu sesión. Después puedes borrar las carpetas desde el Explorador.

### Inicio automático y barra de tareas

Deja `Capture.exe` en la carpeta donde quieras conservarlo y ábrelo una vez. Haz clic derecho en su icono junto al reloj.

- Marca **Iniciar con Windows** para que Capture quede disponible en segundo plano al iniciar sesión. Pulsa de nuevo la misma opción para quitar el inicio automático.
- Elige **Preparar acceso para anclar…**. En el menú **Inicio**, busca **Capture**, pulsa con el botón derecho y elige **Anclar a la barra de tareas**. En algunas versiones de Windows la opción está dentro de **Más**.

Si Capture está solo junto al reloj, el acceso anclado abre el selector. Puedes seguir usando **Ctrl+Alt+R**. Si vas a mover la carpeta, quita primero el inicio automático y el anclaje. Vuelve a configurarlos desde la nueva ubicación.

### Compilar desde el código

Ejecuta este comando en `cmd.exe` desde la raíz del repositorio, con .NET Framework 4.8. Compila un ejecutable de interfaz gráfica e incorpora el icono y el manifiesto de DPI.

```bat
"%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /langversion:5 /codepage:65001 /optimize+ /target:winexe /out:Capture.exe /win32icon:src\Capture.ico /win32manifest:src\Capture.manifest /reference:System.Windows.Forms.dll,System.Drawing.dll src\*.cs
```
