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

## Versión de prueba con atajo y línea

En Windows 10 (versión 1703 o posterior) o Windows 11, abre `Iniciar-Capture.cmd` desde el Explorador. Capture queda disponible en el área de notificación, junto al reloj. Usa Windows PowerShell 5.1 y Windows Forms, incluidos en Windows. El lanzador aplica la política de ejecución solo a su proceso.

- Abre la aplicación o el escritorio que quieras capturar y pulsa **Ctrl+Alt+R**. También puedes abrir el menú del icono de Capture y elegir **Nueva captura**, incluso si otra aplicación ya está usando el atajo. Si el icono está oculto, despliega la flecha junto al reloj.
- Arrastra para seleccionar un área y ajusta sus bordes con los tiradores. Confirma con **Capturar** o **Intro**. **Esc** cancela.
- Para dibujar una **Flecha**, pulsa donde debe empezar y arrastra hasta el punto al que debe señalar. El trazo y la punta se dibujan juntos. **Línea** usa el mismo gesto y dibuja un segmento sin punta.
- Al terminar, el editor vuelve a **Seleccionar**. En una línea o flecha seleccionada, arrastra cualquiera de los dos tiradores para cambiar el ángulo o la longitud. Arrastra el cuerpo para mover la anotación entera. También puedes añadir un **Rectángulo** o **Texto**, cambiar colores, ajustar el grosor de las formas y eliminar anotaciones.
- **Copiar** lleva la imagen anotada al portapapeles. **Guardar** genera un PNG con las dimensiones del recorte.
- **Nueva captura** también está disponible en el editor. Al cancelar el recorte, vuelve la captura anterior. Capture pide confirmación si vas a descartar cambios de anotación que todavía no has copiado ni guardado.
- Para cerrar el programa y liberar el atajo, elige **Salir** en el menú del icono junto al reloj.

En esta iteración el texto se añade en una sola línea y el lienzo ajusta la imagen al espacio disponible. La prueba de pantalla, escalado, portapapeles y guardado requiere una sesión gráfica de Windows.
