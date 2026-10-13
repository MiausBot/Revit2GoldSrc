# Geometría poligonal, registro WAD e iconos

## Resultado de la recuperación

El reporte original registraba 19 elementos encontrados, 8 convertidos y 11 omitidos: 4 pisos y 7 muros. Las capturas de esos 11 sólidos contienen caras planas horizontales/verticales y aristas rectas. Se incorporaron como fixture `model_rejected.json` y los 11 ahora se convierten conservando volumen y límites.

Se generó `C:\Users\GuSs\Downloads\edificio_recuperado_v2.map`: 41 brushes, 246 caras y 19/19 elementos representados. La reconstrucción conserva los 14 brushes del MAP previo y agrega 27 prismas recuperados. Conserva el origen, escala y WAD originales. El reporte identifica explícitamente **captured boundary replay**, no una exportación nueva ejecutada dentro de Revit. Las superficies de material de los omitidos no estaban en el JSON, por lo que esas caras utilizan textura neutra existente; una exportación nueva desde Revit sí lee sus materiales.

La variante independiente `edificio_recuperado_v2_recinto.map` agrega un recinto cerrado y se compiló con CSG → BSP → VIS → RAD. Se publicó `edificio_recuperado_v2_recinto.bsp`, versión 30. La exportación original no se sobrescribió.

```powershell
dotnet run --project Revit2GoldSrc.Sample -- --recover-report "$env:USERPROFILE\Downloads\edificio_diagnostico_export_report.json" "$env:USERPROFILE\Downloads\edificio_diagnostico.map" "$env:USERPROFILE\Downloads\otro_nombre_recuperado.map"
```

El destino y su variante de recinto deben ser nuevos. No se ejecuta Half-Life ni se copia el BSP a su instalación.

## Conversión

`BimElement.Solids` conserva las cajas y sus constructores anteriores. La propiedad adicional `Prisms` contiene `ConvexPrism`: polígono convexo XY, cota inferior y superior. `Vertices` unifica ambos tipos para calcular el origen. El constructor nuevo admite `prisms` como argumento opcional.

La conversión de cajas sigue siendo la primera opción. Si el sólido no es ortogonal o el piso no es rectangular, se intenta la ruta poligonal:

1. Validar caras planas, aristas rectas, contornos cerrados y frontera manifold; rechazar caras inclinadas fuera de tolerancia.
2. Dividir por las cotas Z de los vértices e intersectar las caras verticales con el centro de cada banda.
3. Verificar secciones cerradas, incluidos huecos, y partir su rectángulo de análisis por las rectas de sus segmentos.
4. Conservar únicamente las celdas convexas interiores mediante clasificación par/impar y extruirlas entre cotas.
5. Comprobar volumen original, convexidad, orientación y releer los brushes Valve 220.

El rectángulo solo limita el análisis: nunca se exporta como sustituto del sólido. Los recortes, entradas de puertas, huecos y uniones diagonales se conservan. Las celdas son disjuntas por construcción. Se rechaza cualquier incertidumbre o pérdida de volumen significativa. Límites: 250000 celdas por sección y 4096 prismas por sólido; hasta 126 vértices por prisma (128 planos GoldSrc). Curvas y superficies realmente inclinadas siguen fuera de alcance.

La asignación de material en caras no rectangulares subdivide la cara real por los contornos de las superficies originales. No usa su rectángulo envolvente como cobertura. Las caras internas permanecen neutras. El spawn primero conserva la búsqueda anterior y, si hace falta, verifica soporte sobre la unión de caras superiores coplanarias, para permitir que el jugador atraviese juntas de descomposición sin aceptar huecos reales. Los nuevos prismas se pasan como envolventes conservadoras a la comprobación de puertas; un movimiento incierto se omite con diagnóstico.

## Registro WAD

Después de exportar, **Registrar WAD en J.A.C.K.** valida MAP, WAD3 y nombres de todas las texturas, incluidas las técnicas en sus roles válidos. Detecta `VDKGameCfg.ini`, muestra perfiles Half-Life habilitados y agrega rutas `ParcelN` sin duplicados. Conserva comentarios, otras opciones, perfiles, codificación y saltos de línea.

J.A.C.K. debe estar cerrado. El comando no mata procesos. Comprueba también que el INI no haya cambiado durante la operación y conserva una copia `.bak` mediante reemplazo atómico. Si no puede escribir, guarda un `.propuesto.ini` junto al MAP e indica cómo copiarlo con permisos adecuados.

En esta sesión, después de que el usuario cerrara J.A.C.K., se registró `C:\Users\GuSs\Downloads\edificio_diagnostico_materiales.wad` como `Parcel4` del perfil Half-Life. La copia del INI anterior quedó junto a `VDKGameCfg.ini`, con fecha y GUID. Al abrir J.A.C.K. debe cargar los nombres `R2G_*`. El damero magenta era distinto de los colores del WAD generado, que contiene grises 127/128. La comprobación visual final dentro del editor sigue pendiente.

## Iconos

Nueve diseños vectoriales WPF compilados en el ensamblado: exportación, parámetros de puertas, análisis, recomendaciones, registro WAD, carga WAD, destino, cancelar/cerrar y compilación. El ribbon recibe imágenes rasterizadas transparentes de 16 y 32 píxeles. Las ventanas usan el dibujo vectorial, textos, tooltips y atenuación de imágenes deshabilitadas. No se requieren archivos externos ni paquetes adicionales.

Se renderizaron y revisaron los nueve iconos en fondos claro y oscuro a 100 %, 150 % y 200 %, además de los 18 PNG del ribbon. Vista previa: `artifacts/iconpreview/icons_preview.png`. Esto verifica el recurso WPF, no sustituye la revisión de la pestaña y ventana dentro de Revit. Los botones principales se distribuyen mediante WrapPanel para evitar recortes al estrechar la ventana.

## Validación

- 135 pruebas por runtime, 405 ejecuciones en net48, net8.0 y net10.0: correctas.
- Fixtures reales: 11/11 sólidos recuperados, comprobaciones de volumen y límites, brushes válidos.
- Casos sintéticos: pisos cóncavos girados, hueco en piso, unión de soporte bajo el jugador, huecos sin soporte, material pintado en cara triangular, contorno abierto, volumen incorrecto y pendiente no soportada.
- Registro INI: backup, duplicados, múltiples perfiles, editor abierto, apertura durante escritura, modificación concurrente, archivo protegido, textura inexistente, BOM UTF-8 y saltos de línea.
- Regresiones anteriores: arquitectura compatible, materiales, puertas, publicación y compilación VHLT.
- BSP de la reconstrucción con recinto: compilado e inspeccionado como GoldSrc 30.

Compilación y paquetes de Revit 2024/2025 se verifican aparte. 2026/2027 permanecen configurados sin instalaciones validadas. Pendiente: exportar nuevamente el modelo dentro de Revit, comprobar 19/19 en esa exportación nueva, revisar iconos dentro de Revit y abrir el MAP en J.A.C.K. para revisar apariencia y contornos. La prueba jugable sigue siendo una comprobación separada.
