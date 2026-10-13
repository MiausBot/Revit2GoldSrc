# Puertas y compilación GoldSrc

## Uso desde Revit

1. Con el paquete del año instalado, abra **Revit2GoldSrc → Preparar parámetros de puertas**. Este comando tiene una transacción independiente: crea bindings de instancia de `OST_Doors` y valores predeterminados únicamente para parámetros nuevos. Si detecta nombres, GUID o bindings incompatibles, cancela sin sobrescribirlos. Restaura la ruta de parámetros compartidos de Revit al terminar.
2. Configure las puertas desde sus propiedades de instancia. Los parámetros son opcionales: una puerta sin configuración será estática cuando se habilite su exportación.
3. En **Exportar mapa GoldSrc**, habilite **Exportar puertas**. Está desactivado inicialmente y el MAP conserva el flujo anterior de vanos abiertos. En modo WAD existente seleccione también la textura independiente de puerta; el botón de recomendaciones la rellena si hay una candidata. En modo Materiales de Revit se usan superficies originales, incluidos materiales pintados que la API pueda recuperar.
4. Exporte y revise el reporte. Una puerta animada insegura se omite con su ID y causa; no se reemplaza silenciosamente por una puerta estática.
5. Después de una exportación válida pulse **Compilar MAP a BSP** en la misma ventana. Puede configurar CSG, BSP, VIS y RAD en el desplegable de compiladores. No es necesario abrir J.A.C.K.

## Parámetros de instancia

| Nombre (`GoldSrc_`) | Tipo Revit | Predeterminado / valores |
|---|---|---|
| Animated | Sí/No | No |
| DoorType | Texto | Rotating / Sliding / Static; Rotating por defecto |
| OpenAngle | Ángulo | 90°; intervalo seguro soportado (0°, 180°] |
| Speed | Número | 100; grados/s para abatibles, HU/s para corredizas |
| Wait | Número | 3 segundos; -1 permanece abierta |
| Activation | Texto | Use / Touch / Trigger; Use por defecto |
| ReverseDirection | Sí/No | No |
| HingeSide | Texto | Auto / Left / Right |
| HingeOffsetX, HingeOffsetY | Longitud | 0; correcciones locales de bisagra |
| SlideDirection | Texto | Auto / Left / Right / Up / Down |
| SlideDistance | Longitud | 0 = ancho/altura de hoja + 2 HU; positivo = distancia explícita |
| LeafThickness | Longitud | 40 mm para la hoja simplificada |
| DoorGroup | Texto | Vacío; mismo texto vincula instancias separadas |
| LeafCount | Texto | Auto / Single / Double |
| ActivatorType | Texto | Button / Trigger; Trigger por defecto |
| ActivatorPlacement | Texto | Auto / Manual |
| ActivatorOffsetX, ActivatorOffsetY, ActivatorOffsetZ | Longitud | 0; ubicación local del centro del activador manual |

Los GUID son estables: MD5 de UTF-8 `Revit2GoldSrc.Doors.v1:GoldSrc_<nombre>`, interpretado por `new Guid(byte[])`. El catálogo vive en `Revit2GoldSrc.Revit/DoorParameters.cs`. Las longitudes y ángulos se guardan en unidades internas de Revit (pies/radianes); la UI de Revit presenta las unidades del proyecto. Los parámetros de velocidad y espera son números, no longitudes.

## Marco local y evidencia geométrica

El origen es `LocationPoint` de la instancia. Z es vertical, Y es `FacingOrientation` proyectado horizontalmente y X es `Z × Y`. X positivo es **Right** visto desde el lado de FacingOrientation mirando hacia la puerta; X negativo es **Left**. Las orientaciones nativas y transformaciones se aplican una vez. `HandFlipped`, `FacingFlipped` y reflexión se registran; no provocan una segunda inversión.

Se prefieren hojas ortogonales rectangulares distinguibles del marco y compatibles con ancho/altura de familia. Los demás sólidos verificables se consideran marcos. Si no puede identificarse la hoja, se propone una hoja rectangular con dimensiones de familia y se registra la simplificación. Los sólidos descartados o no evaluables siguen siendo incertidumbre: si afectan el recorrido, se omite la animación. Nunca se usa una caja envolvente como sustituto exportado de geometría desconocida.

Auto de bisagra solo usa evidencia de un subcomponente compartido llamado `hinge`/`bisagra` situado en un extremo. Otras familias deben indicar Left/Right manualmente. Las hojas dobles se ordenan de izquierda a derecha: Auto usa bisagras exteriores; una corrección manual indica el lado de la primera hoja y el opuesto para la segunda. Auto de hojas reconoce dos hojas rectangulares verificables; otras familias necesitan Single/Double explícito. Los sólidos complejos, inclinados o ambiguos pueden omitirse.

Un modelo simplificado de dos hojas sin holgura puede intersecarse durante el giro debido a su espesor. El exportador no reduce dimensiones para evitarlo: ajuste la geometría real/configuración y revise el diagnóstico. También pueden rechazarse movimientos válidos que el análisis conservador no logra demostrar.

El barrido corredizo es un prisma continuo sobre un eje local. El abatible subdivide el arco con cotas conservadoras y un presupuesto limitado; no se limita a probar las posiciones inicial/final. Incluye arquitectura, marcos, otras hojas cerradas, geometría incierta y botones. El análisis de otras hojas es conservador: comprueba contra su posición cerrada y rechaza la superposición de envolventes completas de recorrido, incluso si se activan simultáneamente. Las envolventes del arco incluyen sus extremos y todos los extremos cartesianos de cada vértice. ORIGIN y triggers no son obstáculos físicos. Los botones ya aceptados también restringen puertas posteriores. El recinto de prueba abarca los recorridos, y el spawn comprueba hojas cerradas y botones.

## Entidades originales de Half-Life

Se contrastó el FGD instalado `C:\Program Files\J.A.C.K\halflife\halflife.fgd` con [doors.cpp](https://github.com/ValveSoftware/halflife/blob/master/dlls/doors.cpp), [doors.h](https://github.com/ValveSoftware/halflife/blob/master/dlls/doors.h) y [SetMovedir en subs.cpp](https://github.com/ValveSoftware/halflife/blob/master/dlls/subs.cpp).

- Abatible: `func_door_rotating`, una hoja y un ORIGIN propio. ORIGIN define la bisagra; `angles="0 0 0"` conserva la pose geométrica exportada. Eje Z, `distance`, `speed`, `wait`, flag One-way 16 para fijar el giro y Reverse 2 para su signo. No se usa el flag 4 que aparece en el FGD pero no garantiza aislamiento en el SDK original.
- Corrediza: `func_door`, dirección en `angles`, `lip` calculado con la fórmula del SDK. Tamaño nativo expandido 1 HU por lado: `distancia = sum(abs(movedir[i] * (size[i] - 2))) - lip`. Vertical usa `angles="0 -1 0"` / `"0 -2 0"`. No se emite `distance` en corredizas ni propiedades Source.
- Simple Use: flag 256 y sin `targetname`. Simple Touch: sin flag 256 ni `targetname`, porque un nombre deshabilita `DoorTouch` en Half-Life.
- Dobles/grupos: nombre compartido y entidades independientes. Use genera un `func_button` inmóvil (flag 1); Touch genera un `trigger_multiple`. Trigger utiliza Button/Trigger según la configuración. Los activadores apuntan al nombre compartido; las hojas remotas no ofrecen uso/contacto directo.
- Los grupos requieren tipo, activación, espera, velocidad y ángulo compatibles. El activador toma configuración de la instancia de menor ID. Ubicación automática busca ambos lados; si ninguna posición es segura, configure Manual y offsets. Los botones automáticos se colocan cerca de la jamba; revise su accesibilidad en el juego.
- `wait=-1` no vuelve automáticamente. No se usa el flag Toggle para simular permanencia.

Los brushes técnicos tienen roles explícitos. El exportador genera su propio `<mapa>_tools.wad` con ORIGIN y AAATRIGGER. Se permite cada textura únicamente en su contexto. Las texturas arquitectónicas siguen exigiendo un WAD/asignación válida. MAP, WAD de materiales, WAD técnico y reporte se publican juntos con restauración de archivos anteriores ante fallos.

## Compilar y reproducir la escena

El módulo `Revit2GoldSrc.Compilation` es independiente de Revit. Ejecuta los cuatro `.exe` sin shell, con argumentos Windows escapados individualmente (compatible con net48), captura stdout/stderr y permite cancelación. Copia el MAP y sus WAD a `.Revit2GoldSrc-build/<fecha_GUID>` junto al MAP; incrusta las texturas utilizadas. Las rutas WAD relativas se resuelven respecto al MAP original.

Cada etapa guarda `CSG.log`, `BSP.log`, `VIS.log`, `RAD.log`; `compilation_report.json` identifica rutas, exit codes, tiempos, error y publicación. Un fallo, fuga o cancelación detiene la cadena. Se conserva el BSP anterior. Los archivos de trabajo y logs quedan para diagnóstico. Solo después de RAD y validación de header/lumps/modelos BSP30 se publica el `.bsp` junto al MAP. No inicia Half-Life ni copia a su instalación.

```powershell
dotnet run --project Revit2GoldSrc.Sample -- --doors Examples/doors_demo.map
dotnet run --project Revit2GoldSrc.Sample -- --compile-doors Examples/doors_demo.map
dotnet test Revit2GoldSrc.Tests
.\scripts\Build.ps1 -Versions 2024,2025
```

La escena cerrada de C# contiene piso, techo, paredes, dos aberturas, una abatible Use, una corrediza Touch, jugador y luz. Sus dimensiones son HU: en este fixture se usa 1 HU por unidad de entrada para que la inspección numérica sea directa. Produce 14 brushes MAP, 84 caras, 5 entidades y 3 modelos BSP (mundo + dos puertas). ORIGIN se consume durante CSG: el BSP tiene bisagra `-82 1 40`, `distance=90`, flags 272; la corrediza tiene ancho 36, desplazamiento 40 y `lip=-4`.

## Validación y límites de aceptación

Verificado automáticamente: 118 casos por runtime (354 ejecuciones) en .NET Framework 4.8, .NET 8 y .NET 10. Incluyen giros/reflexiones, correcciones, barridos, contactos, activación de dobles, materiales pintados independientes de API, publicación/restauración de archivos, lectura WAD/MAP y compilación real VHLT de ambas puertas. Los tests de integración requieren los VHLT locales; si faltan, fallan con diagnóstico en lugar de afirmar que la compilación fue probada.

La escena fue compilada con VHLT v34 (2015/8/17), sin J.A.C.K., y se inspeccionaron BSP30, modelos y entidades. Compilar DLL contra API 2024/2025 no demuestra carga o ejecución dentro de Revit. Sigue pendiente probar los nuevos comandos con familias reales en Revit y recorrer el BSP en Half-Life para verificar animación, colisiones, uso y cierre. 2026/2027 permanecen configurados sin validación de sus instalaciones. Los 11 elementos previamente omitidos se recuperan en la ampliación poligonal documentada en [GeometryRecovery.md](GeometryRecovery.md); sus envolventes se consideran incertidumbre conservadora para el movimiento de puertas.
