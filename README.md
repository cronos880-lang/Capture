# Capture

Aplicación pequeña de escritorio para Windows que permite **capturar una zona de la pantalla y explicarla en la propia imagen con flechas y texto**. «Captura y anota» es el nombre provisional que aparece en las maquetas. El proyecto está en **fase de diseño visual**.

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

Las imágenes son maquetas generadas para visualizar el flujo y usan contenido de ejemplo. La revisión del diseño precede a la implementación.

## Primera versión de prueba

En Windows 10 (versión 1703 o posterior) o Windows 11, abre `Iniciar-Capture.cmd` desde el Explorador. Usa Windows PowerShell 5.1 y Windows Forms, incluidos en Windows. El lanzador aplica la política de ejecución solo a su proceso.

- Arrastra para seleccionar un área y ajusta sus bordes con los tiradores. Confirma con **Capturar** o **Intro**. **Esc** cancela.
- Usa **Rectángulo**, **Flecha** o **Texto**. Al terminar, el editor vuelve a **Seleccionar**. Puedes arrastrar una anotación, cambiar su color, ajustar el grosor de las formas o eliminarla.
- **Copiar** lleva la imagen anotada al portapapeles. **Guardar** genera un PNG con las dimensiones del recorte.
- **Nueva captura** abre otra selección. Guarda o copia la captura actual antes de usarla si quieres conservarla.

En esta iteración el texto se añade en una sola línea y el lienzo ajusta la imagen al espacio disponible. La prueba de pantalla, escalado, portapapeles y guardado requiere una sesión gráfica de Windows.
