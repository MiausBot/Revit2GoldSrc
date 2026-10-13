# Familias, MEP y vínculos

## Uso

1. Abra **Exportar mapa GoldSrc** y seleccione **Modelo completo con materiales**. El perfil arquitectónico anterior sigue disponible como opción inicial.
2. El nuevo perfil activa Materiales de Revit y muestra categorías físicas encontradas en el documento y sus vínculos cargados. Puede buscar, seleccionar todo o desmarcar todo. Las categorías muestran cantidades; anotaciones y elementos sin sólidos/mallas no cuentan como geometría física exportable.
3. Elija **Equilibrado** o **Máximo detalle**. Equilibrado es el predeterminado del perfil nuevo. Habilite/deshabilite vínculos según corresponda.
4. Cada categoría permite **Sólido / Sin colisión**; Sólido es el predeterminado. En modo WAD existente se habilita además la asignación de textura por categoría. Las recomendaciones pueden rellenarla, y puede ajustarla manualmente.
5. Analice/exporte. Las puertas mantienen **Exportar puertas** y sus parámetros como opción independiente, desactivada inicialmente. No se duplican por el extractor general.
6. Después de publicar, registre el WAD en J.A.C.K. con el botón existente (editor cerrado) y/o compile por consola con los cuatro VHLT.

La extracción es de solo lectura. No abre vínculos descargados, no modifica familias ni bindings y no automatiza J.A.C.K.

## Alcance y procedencia

Se descubren categorías de modelo con sólidos o mallas: tuberías, accesorios, equipos, conductos, mobiliario, ventanas, modelos genéricos y otras categorías físicas, incluidas familias de sistema. No se presupone una caja rectangular ni una orientación vertical para las nuevas geometrías. Anotaciones, niveles, ejes, grupos contenedores, espacios y referencias se excluyen.

Las instancias anidadas componen transformaciones una vez. Un subcomponente compartido se omite como duplicado solo si aparece en la geometría del padre con el mismo símbolo y transformación. Las mallas nativas/importadas se intentan convertir si están cerradas; superficies abiertas o dibujos sin volumen no se rellenan ni engrosan automáticamente.

Los vínculos cargados y anidados aplican `GetTotalTransform()` y la cadena de transformaciones. Se detectan ciclos. Una instancia de vínculo seleccionada incluye sus elementos de las categorías habilitadas. Cada elemento conserva documento, nombre del documento, cadena de IDs de instancias vinculadas e ID BIM de 64 bits. Vínculos repetidos mantienen identidades distintas aunque su ID de elemento coincida. Las categorías internas con IDs positivos se distinguen por documento.

Los materiales usan clave documento + ID. Se comparte un WAD con imágenes equivalentes deduplicadas; materiales distintos de documentos diferentes no se mezclan por compartir ID. Pintura accesible, apariencia, imagen, color, tamaño y offsets se leen en el documento de origen. La asociación de triángulos se comprueba contra sus superficies originales. Una asociación/material coplanar ambiguo se informa y usa gris; las caras interiores nuevas son neutras. El mapeado sobre curvas facetadas sigue siendo una aproximación de apariencia, no UV nativo de Revit ni render PBR.

Las puertas vinculadas tienen grupos separados por documento/cadena de vínculo. Se transforma geometría, materiales y marcos. Una animación en vínculo reflejado o inclinado se omite con diagnóstico porque su sentido de apertura no se garantiza. Las puertas estáticas permanecen exportables cuando su geometría puede verificarse.

## Calidad y descomposición

| Perfil | Precisión solicitada | Nivel de detalle inicial | Error relativo máximo de volumen |
|---|---:|---:|---:|
| Equilibrado | 5 mm | 0,5 | 3 % |
| Máximo detalle | 1 mm | 1 | 1 % |

Las rutas anteriores de cajas y prismas se conservan primero. La geometría que no admite esas rutas pasa a `SolidUtils.TessellateSolidOrShell`. Se comprueba el volumen contra el `Solid.Volume` original y la desviación mediante centros/aristas de triángulos y muestras UV de superficies originales hacia la malla. Se permiten tres intentos: aumentar detalle hasta 1 y reducir la precisión solicitada a la mitad por intento. No se cambia de perfil silenciosamente.

Las mallas cerradas se descomponen mediante cortes BSP en C#. Se cierran cortes con polígonos planos, conservando huecos, orientación y volumen; se fusionan caras coplanares compatibles dentro de cada pieza. Se intenta unir piezas convexas adyacentes con una cara completa compartida, solo si mantienen volumen, materiales y límites. Esta optimización tiene un presupuesto de 2 segundos y se omite para más de 256 piezas sin reducir la calidad. Las piezas de lados opuestos al corte son disjuntas. No se usa un hull envolvente para sustituir un sólido cóncavo. Una tubería hueca conserva el interior si está presente en la geometría original.

Presupuestos actuales: 24000 triángulos y 12000 vértices de análisis por sólido; 8192 nodos de división, profundidad 64 y 4096 brushes finales; 128 planos por brush. Análisis de superficies y descomposición tienen presupuestos de 30 segundos, con puntos de cancelación entre operaciones. Una llamada nativa a teselación de Revit debe regresar antes de poder cancelar.

La compilación real detectó una restricción adicional del VHLT instalado: un polígono de 32 vértices falla con `MAXPOINTS`. Los sólidos convexos se subdividen también cuando alguna cara tiene 32 o más vértices, conservando su volumen. Hay pruebas específicas para cilindros de 32/64 lados.

Un presupuesto excedido, malla abierta, orientación inconsistente, desviación excesiva, volumen incorrecto o cierre incierto causa omisión con documento, elemento y motivo. La selección de todas las categorías no garantiza que cualquier familia pueda convertirse ni que un modelo arbitrariamente grande quepa en GoldSrc.

## Colisiones y reporte

Los elementos sólidos estáticos quedan en `worldspawn`. Los decorativos se agrupan por documento y categoría en `func_illusionary` con `skin=-1`, conforme al FGD instalado y al [SDK original](https://github.com/ValveSoftware/halflife/blob/master/dlls/bmodels.cpp). No bloquean la comprobación del jugador ni el movimiento de puertas. Los sólidos convexos generales y prismas nuevos se consideran envolventes conservadoras al verificar puertas, pudiendo causar una omisión por incertidumbre.

Una exportación formada exclusivamente por decoración requiere arquitectura sólida o recinto de prueba para disponer de worldspawn válido. Los elementos sin suelo compatible no generan un jugador dentro de geometría.

El reporte añade `Elements`: origen, categoría real, familia/tipo, colisión, estado, calidad, error de volumen, desviación superficial y brushes. `MaterialExportInfo.SourceDocument` conserva el origen material. Las propiedades antiguas se mantienen: `BimElement.Solids`, `Prisms`, ID, categoría arquitectónica y constructores previos. Se añade `ConvexSolids`; las categorías arbitrarias utilizan `CategoryKey`/`CategoryName` y un registro de texturas por clave.

## Pruebas y entrega

- 152 casos por runtime en net48, net8.0 y net10.0: 456 ejecuciones correctas.
- Casos de malla: cilindro, tubería hueca, codo, reflejos/giros, equipo cóncavo inclinado, malla abierta/no finita, tapas de alta segmentación y distancia superficial.
- Materiales con ID 52 en dos documentos: colores y texturas diferentes conservados. Identidades de vínculos repetidos con IDs de 64 bits comprobadas.
- Decoración agrupada por documento/categoría y excluida de colisiones del spawn comprobada.
- Regresiones arquitectónicas, recuperación de los 11 omitidos, puertas, WAD, registro INI y restauración/publicación siguen pasando.
- `Examples/families_demo.map` y su WAD/BSP: escena C# con piso, tubería hueca horizontal, tubería girada con vínculo simulado, codo, equipo girado, mobiliario decorativo y recinto. Se compiló CSG → BSP → VIS → RAD. BSP30 con dos modelos (mundo y decoración), jugador y luz. Su reporte dice explícitamente que los vínculos son simulados.

```powershell
dotnet run --project Revit2GoldSrc.Sample -- --compile-families Examples/families_demo.map
dotnet test Revit2GoldSrc.Tests
.\scripts\Build.ps1 -Versions 2024,2025
```

Se comprueban por separado las compilaciones de adaptador 2024/2025. No se ha ejecutado aún el nuevo perfil dentro de Revit con familias/vínculos reales. Esa aceptación requiere comparar modelos reales, pintura, vínculos repetidos/anidados/reflejados/descargados, selección y ambos niveles, además de abrir el MAP en J.A.C.K. y revisar colisiones en Half-Life. 2026/2027 permanecen configurados sin validación de sus instalaciones.
