# Revit2GoldSrc

Complemento C# para exportar arquitectura básica de Autodesk Revit a sólidos brush convexos en archivos `.MAP` Valve 220 para GoldSrc (Half-Life / Counter-Strike 1.6). Lee el modelo sin modificarlo. No exporta una malla visual ni genera VMF.

## Versiones y requisitos

| Revit | Framework del complemento | Estado en este equipo |
|---|---|---|
| 2024 | .NET Framework 4.8 (`net48`) | Compilado contra las DLL instaladas |
| 2025 | .NET 8 Windows | Compilado; dos advertencias MSB3277 de referencias Autodesk |
| 2026 | .NET 8 Windows | Configurado; faltan las DLL para compilar/verificar |
| 2027 | .NET 10 Windows | Configurado; faltan las DLL para compilar/verificar |

Windows x64, SDK .NET 10 para la solución y la herramienta de ejemplo, runtimes .NET 8/10 para las pruebas, Developer/Targeting Pack .NET Framework 4.8, y las DLL `RevitAPI.dll`/`RevitAPIUI.dll` del año elegido. El framework de cada complemento se selecciona en compilación; al cargarlo se comprueba el año anfitrión. No se puede usar el mismo binario para todos los años.

Core y GoldSrc usan .NET Standard 2.0, sin referencias Autodesk ni dependencias comerciales. Las pruebas usan xUnit. No se requiere Blender, AutoCAD, Unreal ni un compilador BSP para exportar.

Fuentes de compatibilidad: [Autodesk 2024](https://help.autodesk.com/cloudhelp/2024/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Getting_Started/Welcome_to_the_Revit_Platform_API/Revit_API_Revit_API_Developers_Guide_Introduction_Getting_Started_Welcome_to_the_Revit_Platform_API_Development_Requirements_html.html), [2025](https://help.autodesk.com/cloudhelp/2025/ENU/Revit-WhatsNew/files/GUID-50024FD2-16BE-40BE-96E6-550294D9537D.htm), [2026](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API/files/Revit_API_Developers_Guide/Introduction/Getting_Started/Welcome_to_the_Revit_Platform_API/Revit_API_Revit_API_Developers_Guide_Introduction_Getting_Started_Welcome_to_the_Revit_Platform_API_Development_Requirements_html.html), [2027](https://help.autodesk.com/cloudhelp/2027/ESP/Revit-WhatsNew/files/GUID-8D7A4715-EAF8-4BD1-BE78-061F900D0BCE.htm).

## Compilar e instalar

Desde PowerShell en la raíz:

```powershell
./scripts/Build.ps1 -Versions 2024,2025
# Cerrar la versión de Revit antes de instalar:
./scripts/Install.ps1 -Version 2024
./scripts/Install.ps1 -Version 2025
```

Los paquetes quedan en `artifacts/2024` y `artifacts/2025`. La instalación copia únicamente el complemento, sus dos bibliotecas y los símbolos/deps disponibles a `%APPDATA%/Autodesk/Revit/Addins/<año>/Revit2GoldSrc`. Registra `Revit2GoldSrc.addin` con una ruta absoluta a la DLL. No copia las DLL de Autodesk. Reinicie Revit y acepte la carga del complemento si Revit lo solicita.

Para una instalación o SDK en otra ubicación:

```powershell
./scripts/Build.ps1 -Versions 2026 -RevitInstallDir 'D:/SDK/Revit2026'
./scripts/Build.ps1 -Versions 2027 -RevitInstallDir 'D:/SDK/Revit2027'
```

Las DLL ausentes o pertenecientes a otro año producen un error explícito. La compilación directa también admite ambas propiedades:

```powershell
dotnet build Revit2GoldSrc.Revit -c Release -p:RevitVersion=2024
```

La solución usa 2024 por defecto. Los archivos intermedios y resultados Revit se separan por año, incluyendo 2025 y 2026 aunque compartan framework. Los manifiestos fuente están en `Addins/`. Para instalar manualmente copie las cuatro DLL del paquete a una carpeta propia y edite `Assembly` en el manifiesto antes de colocarlo en la carpeta Addins del año correspondiente.

Para desinstalar, con Revit cerrado elimine únicamente `Revit2GoldSrc.addin` y su carpeta `Revit2GoldSrc` de esa carpeta Addins.

## Exportar en Revit

1. Abra un proyecto y pulse **Revit2GoldSrc → Exportar mapa GoldSrc**.
2. Seleccione todo el modelo arquitectónico o los elementos seleccionados, y habilite muros, pisos/losas y columnas según corresponda.
3. Pulse **Analizar modelo**. Se verifican sólidos, contornos y volumen sin escribir archivos; las omisiones se muestran con identificadores.
4. Seleccione destino/nombre `.map`. La escala predeterminada es 12 HU/pie: un metro equivale a 39.37007874 HU. La tolerancia predeterminada es 0.00001 pies.
5. Centre el origen en los elementos compatibles o introduzca X/Y/Z en pies del sistema interno de Revit. Z permanece vertical; no se altera la orientación del edificio.
6. Cargue un WAD3 existente y seleccione una textura real para cada categoría habilitada. **Rellenar con texturas recomendadas** abre una ventana para completar solo campos vacíos, reemplazar todas las asignaciones o cancelar. Incluye también categorías desactivadas y la textura del recinto; cada recomendación sigue siendo editable. Puede usar `Examples/test.wad` y la textura **TEST** para una prueba inicial.
7. Active jugador/iluminación y, para un primer ensayo, el recinto cerrado. Asigne también su textura. El recinto se coloca fuera de los límites exportados, con 128 HU de margen y 16 HU de espesor.
8. Pulse **Exportar MAP**. La ventana muestra progreso, permite cancelar entre elementos y presenta las estadísticas y advertencias.

El destino inicial es `Descargas/edificio_diagnostico.map`, para conservar la exportación anterior. Se escriben `nombre.map` y `nombre_export_report.json`. Antes de publicar se serializa y relee el mapa, reconstruyendo cada brush desde sus planos. Se comprueban cierre, convexidad, volumen, ejes de textura, sintaxis y coordenadas finitas. La publicación utiliza archivos temporales y restaura el mapa anterior si falla la escritura del reporte; no es una transacción resistente a pérdida de energía entre ambos reemplazos.

El reporte incluye versión de Revit, origen, escala, tolerancia, cantidades, duración del análisis/conversión/validación e identificadores de 64 bits con motivos de omisión. El modo de materiales añade materiales utilizados, imágenes, sustituciones, escala/rotación/desplazamientos y conteos por cara. Los errores que impiden iniciar/completar la exportación se muestran en la ventana. Si todos los elementos son omitidos, se publica únicamente el reporte de diagnóstico con `MapPublished=false`, sin crear ni reemplazar el MAP. La cancelación no publica resultados nuevos.

## Geometría admitida

- Muros básicos con eje recto horizontal y superficies ortogonales en el marco del muro, incluidos muros rotados.
- Vanos rectangulares reales de puertas y ventanas. Se analiza la geometría recortada del anfitrión: el muro se divide en prismas y la abertura queda vacía. Las hojas y cristales no se exportan como obstáculos.
- Losas horizontales rectangulares, incluidas las rotadas, y columnas verticales con sección rectangular.
- Geometría de instancias anidadas con composición de transformaciones; los sólidos directamente devueltos por Revit conservan su posición.

Los muros se clasifican por celdas en un marco local usando rayos contra sus caras planas y todos los contornos, incluidos agujeros. Se agrupan celdas adyacentes en prismas sin ocupar los vanos y se compara el volumen total con `Solid.Volume`. Columnas y losas deben producir un prisma por sólido. Si falla cualquier sólido del elemento, se omite el elemento completo.

**Limitaciones:** muros curvos, inclinados, cortina/apilados, remates o uniones diagonales, aristas curvas, losas con huecos/pendientes o contornos irregulares, columnas inclinadas, escaleras, rampas, techos, mobiliario y vínculos. Algunos muros unidos pueden quedar omitidos por geometría de sus extremos: revise el reporte. Los elementos incompatibles nunca se sustituyen por sus cajas envolventes. El MVP no elimina superficies entre elementos diferentes.

Los vínculos se excluyen del ámbito completo; seleccionar un vínculo produce una advertencia de categoría no soportada. No se recorre su documento ni se aplica su transformación en este MVP.

El análisis admite hasta 250000 celdas por sólido y la búsqueda de spawn hasta 20000 candidatos; son protecciones de recursos de esta implementación, **no límites del motor**. Un spawn exige soporte para el hull de pie de Half-Life (32 × 32 × 72 HU) y ausencia de intersecciones conservadoras. Puede rechazarse un espacio transitable si no aparece entre los puntos muestreados. Si no se encuentra espacio, se informa y no se generan jugador/luz; el archivo sigue siendo editable.

## WAD y texturas

El lector verifica directorio, mipmaps, nombres y paletas de WAD3 sin cargar texturas en memoria. Solo permite texturas presentes y contenido sólido normal. El MVP rechaza texturas especiales como `sky`, `clip`, `origin`, `hint`, `skip`, `aaatrigger` y líquidos `!` para evitar cambiar el contenido de los brushes accidentalmente. Las escalas UV son 1 con ejes locales consistentes y desplazamientos cero.

No se redistribuyen recursos de Half-Life. El pequeño WAD TEST del ejemplo es original y únicamente un recurso de prueba. El modo **Materiales de Revit** genera un WAD propio; el modo **WAD existente** conserva el flujo de selección manual. Las recomendaciones usan nombres: WALL/BRICK/PLASTER para muros, FLOOR/FLR/TILE para pisos, COLUMN/PILLAR/CONCRETE/CRETE/METAL para columnas; el recinto usa la recomendación de muros. Se prefieren texturas normales sobre variantes animadas, transparentes y nombres de interruptores/carteles. Los empates se resuelven alfabéticamente. Cuando no hay coincidencia útil se avisa de la alternativa genérica. No se analizan imágenes ni materiales BIM.

El subconjunto MAP exportado admite propiedades ASCII y rutas WAD con barras `/`; una ruta WAD con caracteres fuera de ASCII o comillas se rechaza. Use una ruta ASCII para ese archivo.

## Ejemplo y pruebas automatizadas

```powershell
dotnet test Revit2GoldSrc.Tests -c Release
dotnet run --project Revit2GoldSrc.Sample -- Examples/test.wad Examples/room.map
```

Las pruebas se ejecutan en `net48`, `net8.0` y `net10.0`. Cubren dimensiones, unidades, rotación y reflexión, vanos, contornos con agujeros, volumen, geometría inválida, planos invertidos/ausentes/duplicados, sintaxis, culturas numéricas, WAD, spawn y escritura/restauración de archivos.

La herramienta de ejemplo usa el mismo exportador y genera una habitación de 4 × 5 m, puerta de 0.90 × 2.10 m y recinto exterior. Consulte `Examples/README.md`. Regenerar el mapa actualiza la ruta WAD absoluta si cambia la ubicación del repositorio.

## Abrir en J.A.C.K. / Hammer y jugar

1. Configure un perfil **GoldSrc / Half-Life**, no Source, y agregue el WAD seleccionado en las opciones de texturas del editor.
2. Abra el `.map` como Valve 220, revise brushes y vanos y ejecute el comprobador de problemas del editor.
3. Configure compiladores GoldSrc que admitan coordenadas decimales (por ejemplo una versión adecuada de VHLT). Revise su documentación y límites concretos. El antiguo `qcsg` del SDK original lee las coordenadas como enteros; no sirve para preservar estas dimensiones decimales sin cambios.
4. Compile en orden CSG → BSP → VIS → RAD. Un recinto cerrado evita fugas al exterior, pero no garantiza que cada habitación interior tenga iluminación o conectividad suficiente.
5. Compruebe los logs; si hay un leak, siga el pointfile del compilador. Las áreas desconectadas del recinto pueden generar avisos de zonas sin entidades.
6. Copie el `.bsp` al directorio `valve/maps` de su instalación de Half-Life, y asegure que las texturas estén incrustadas por el compilador o que el WAD esté disponible para el juego.
7. Desde la consola del juego ejecute `map nombre`. Compruebe suelo, colisiones, puerta y ventana. La geometría respeta escala arquitectónica; un vano físicamente menor que el hull del jugador seguirá siendo demasiado pequeño para atravesarlo.

No se ejecutan compiladores automáticamente. Escaleras, rampas, niveles/sectores, vínculos, optimización, puertas `func_door` y automatización BSP se reservan para las siguientes fases.

## Depuración y estado de validación

Abra `Revit2GoldSrc.sln` en un Visual Studio compatible con el SDK instalado. Compile el año deseado, instale Debug con `Build.ps1 -Configuration Debug`, y adjunte el depurador a `Revit.exe`: CLR .NET Framework para 2024 y .NET moderno para 2025+. Coloque breakpoints en `ExportCommand`, `RevitGeometryExtractor` y `Exporter`. Desde Codex puede ejecutar los mismos comandos `dotnet` y revisar el JSON; la API Revit se ejecuta en su hilo principal dentro del comando modal, nunca en un worker.

No se ha ejecutado el complemento dentro de Revit ni se ha abierto el mapa en J.A.C.K., compilado un BSP o jugado el resultado durante el desarrollo inicial. La compilación y las pruebas del núcleo no sustituyen esos pasos. Vea `Documentation/VALIDATION.md` para la comprobación manual pendiente.

Las dos advertencias de Revit 2025 se refieren a `Microsoft.VisualBasic` y `System.Drawing`, dependencias transitivas de las DLL Autodesk; se conservan visibles. Autodesk documenta [MSB3277 en la migración a .NET 8](https://help.autodesk.com/view/RVT/2025/ENU/?caas=caas%2Fblog%2Fthebuildingcoder.typepad.com%2Fblog%2F2024%2F04%2Fmigrating-from-net-48-to-net-core-8.html). El código propio no usa esas APIs.

## Diagnóstico de elementos omitidos

El reporte de esquema 2 incorpora códigos de causa y `Geometry` por elemento omitido. Registra todos los sólidos capturados, su volumen original en pies cúbicos, tolerancia, marco local, caras y contornos con extremos de aristas en pies internos de Revit (locales y globales). Los índices de sólido/cara/contorno/arista empiezan en cero. Las aristas curvas conservan tipo y muestras globales como evidencia; no se convierten en brushes mediante esas muestras.

Los códigos `diagonal_edge`, `short_edge` e `invalid_contour` separan los fallos antes agrupados como «Arista no rectilínea o degenerada». Una cara puede tener varias causas. Las caras curvas, los fallos de captura y las incompatibilidades del marco se informan por separado; en esos casos la captura puede ser parcial o carecer de coordenadas locales. Si falla un sólido, se omite el elemento completo y se registran también los demás sólidos capturados del elemento.

`SolidGeometryDiagnostic.Replay()` reproduce la descomposición desde un sólido del reporte sin cargar la API de Revit; permite construir pruebas con los contornos reales. Las geometrías curvas o sin marco local se conservan para inspección pero no son reproducibles por el descomponedor ortogonal.

La búsqueda de spawn informa `spawn_no_compatible_floor`, `spawn_insufficient_space` o `spawn_candidate_limit`. Esta actualización mejora el diagnóstico, no amplía la geometría admitida ni modifica automáticamente la tolerancia.

Para diagnosticar **MODELO CB.rvt**, vuelva a exportar en `Descargas/edificio_diagnostico.map`. Revise el nuevo `edificio_diagnostico_export_report.json`, incluso si no se pudo generar el mapa. La exportación anterior `edificio.map` permanece disponible.

## Materiales de Revit → WAD propio

En **Origen de las texturas**, seleccione **Materiales de Revit (WAD propio)**. No necesita cargar un WAD ni seleccionar texturas por categoría. El modo **WAD existente** sigue siendo el predeterminado.

La exportación lee los materiales de las caras planas reales, consulta pintura sobre sus caras originales cuando la API lo permite y recupera el color o la imagen del recurso de apariencia. Admite un bitmap conectado directamente a propiedades conocidas de color/difuso (principalmente Generic/UnifiedBitmap). Busca rutas originales, rutas relativas al proyecto, bibliotecas de Revit, carpetas estándar de Autodesk y `AdditionalRenderAppearancePaths` de Revit.ini. Un recurso vacío, procedural, inaccesible o una imagen ausente se sustituye por el color del material con advertencia.

WPF decodifica las imágenes y el núcleo independiente genera las texturas. Se crean imágenes opacas de hasta 256 píxeles por lado, dimensiones múltiplos de 16, paleta de 256 colores y cuatro mipmaps. La cuantización median-cut es determinista; las texturas equivalentes se deduplican. Los nombres son ASCII y se derivan del contenido para evitar colisiones entre materiales con el mismo nombre. La transparencia de una imagen se aplana sobre el color del material; no cambia las colisiones del brush.

Las caras se asignan comprobando plano y cobertura completa del contorno, incluidos agujeros. Si una cara mezcla materiales o solo está parcialmente cubierta, se usa gris neutro con advertencia. Las caras internas generadas y el recinto también usan gris neutro.

Se conserva el tamaño físico de repetición, la rotación y el desplazamiento del bitmap. El origen común por elemento mantiene continuidad entre brushes. La posición exacta del dibujo puede diferir de la visualización de Revit, especialmente en instancias y caras con orientación opuesta. No reproduce reflejos, mapas de relieve, iluminación renderizada, recortes alfa ni shaders complejos.

Salidas:

- `nombre.map`
- `nombre_materiales.wad`
- `nombre_export_report.json`

Se validan el MAP y el WAD antes de publicar. Si falla una instalación de archivos, se intenta restaurar los tres anteriores y se conservan backups si la restauración falla. No es una transacción resistente a pérdida de energía. El MAP contiene una ruta WAD absoluta; después de mover los archivos ajuste esa propiedad en J.A.C.K. Use una carpeta de salida ASCII, porque este serializador exige propiedades MAP ASCII.

`Materials` del reporte identifica materiales, texturas, imágenes, proporción de mezcla de color/imagen, sustituciones, tamaños físicos y caras pintadas cuando se pudieron reconocer. Las consultas de pintura que Revit no permite se registran como `paint_lookup_unavailable`.

Ejemplo sintético sin recursos externos ni Revit:

```powershell
dotnet run --project Revit2GoldSrc.Sample -- --materials Examples/material_room.map
```

Esta funcionalidad no recupera los 11 elementos geométricos omitidos de MODELO CB. La fidelidad visual debe comprobarse con una exportación real y en J.A.C.K.; las pruebas automatizadas no sustituyen esa revisión.

## Puertas y BSP

Puertas estáticas, abatibles y corredizas opcionales, parámetros de instancia y compilación CLI mediante VHLT: consulte [Puertas y compilación](Documentation/Doors.md). La opción Exportar puertas está apagada inicialmente. El ejemplo [doors_demo.map](Examples/doors_demo.map) y su BSP son generados por C#; no requieren automatizar J.A.C.K.

## Recuperación de pisos y muros

La conversión admite contornos poligonales rectos y uniones diagonales mediante prismas convexos. Se incluye el botón para registrar WAD en J.A.C.K. e iconos vectoriales para las acciones. Consulte [Geometría, texturas e iconos](Documentation/GeometryRecovery.md) para resultados, pruebas y límites.

## Familias, MEP y vínculos

El perfil **Modelo completo con materiales** descubre categorías físicas, admite vínculos cargados y ofrece Equilibrado/Máximo detalle y colisión por categoría. Consulte [Familias y MEP](Documentation/Families.md) para presupuestos, pruebas y límites.


## Segundo exportador: Source / Left 4 Dead 2

Revit2Source añade VMF, materiales VMT/VTF, puertas Source y compilación VBSP/VVIS/VRAD con Left 4 Dead 2 Authoring Tools, conservando el exportador GoldSrc. Abre **Revit2GoldSrc → Source / L4D2 → Exportar mapa Left 4 Dead 2**. La exportación del documento sigue siendo de solo lectura; posiciones y puertas se guardan en JSON lateral.

[Uso, instalación y límites de Revit2Source](Documentation/Source.md). La escena de referencia está en `Examples/Source/revit_l4d2_room.vmf` y su BSP fue compilado con herramientas oficiales. Las pruebas automatizadas pasan en net48, .NET 8 y .NET 10; Revit 2024/2025 están compilados. La revisión real dentro de Revit y el juego, Nav Mesh y Director siguen pendientes. No se afirma funcionamiento de infectados.
