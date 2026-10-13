# Revit2Source — Left 4 Dead 2

La primera versión genera VMF mediante C#, materiales VMT/VTF propios y BSP mediante Left 4 Dead 2 Authoring Tools. No abre Hammer. El exportador GoldSrc conserva su panel, formatos, opciones y comandos.

## Estado comprobado

- Revit 2024 y 2025: adaptador compilado contra las DLL de cada instalación. Revit 2025 conserva dos advertencias MSB3277 de dependencias Autodesk.
- 197 pruebas por runtime: .NET Framework 4.8, .NET 8 y .NET 10; 591 correctas, sin omisiones en este equipo.
- La escena C# de referencia se exportó y compiló realmente con VBSP → VVIS → VRAD de `E:\SteamLibrary\steamapps\common\Left 4 Dead 2\bin`.
- BSP Source versión 21, estructura de lumps L4D2 comprobada: dos modelos, mundo y puerta abatible. VIS e iluminación LDR/HDR presentes.
- Escena: habitación interior de 4 × 5 m, 3 m de altura, muros exteriores, piso, techo y partición interior con vano de 0,90 × 2,10 m. Nueve elementos arquitectónicos, diez sólidos, 60 caras y 18 entidades. Cuatro supervivientes, cuatro pistolas, cuatro botiquines y dos luces. La partición permite probar la puerta sin abrir el recinto hacia el vacío.
- Entidades, propiedades, conexiones y flags comprobados contra el FGD instalado y sus bases. `Light` es una clase base diferente de la entidad `light`; el lector respeta esa distinción.
- La escena es sintética: no se presenta como exportación real de un documento RVT.
- La ventana real dentro de Revit, navegación, comportamiento de puertas, recursos y Director en L4D2 todavía requieren revisión manual. No se declara funcionamiento de infectados.

## Uso desde Revit

1. Abre Revit 2024 o 2025 después de instalar el paquete del año correspondiente.
2. En la pestaña Revit2GoldSrc, abre **Source / L4D2 → Exportar mapa Left 4 Dead 2**.
3. Elige elementos seleccionados o arquitectura del documento anfitrión. Incluye piso y techo para una habitación cerrada. El lector intenta paredes, pisos, techos, cubiertas, columnas, ventanas estáticas y escaleras con tramos y descansos, sin duplicar componentes representados en su padre.
4. La escala predeterminada es 12 unidades Source por pie de Revit, con Z vertical y origen centrado. El origen manual está en pies internos de Revit. No se cambia automáticamente la escala para ajustarse al motor.
5. Selecciona un destino ASCII, por ejemplo `mi_habitacion.vmf`. Mantén Equilibrado inicialmente. Las geometrías inseguras se omiten y se informan.
6. Añade puertas con **Seleccionar puerta Revit**. Al seleccionar puntos/puertas, la ventana se cierra temporalmente; el comando realiza la selección dentro del contexto API y vuelve a abrirla con su estado. No se utilizan eventos de API fuera del comando.
7. Configura Rotating, Sliding o Static; bisagra Auto/Left/Right; ángulo, velocidad, espera e inversión. Para corredizas indica Left/Right/Up/Down y recorrido positivo en pies. `Wait=-1` mantiene la puerta abierta. Las puertas dinámicas de esta versión se abren con uso; no se reutilizan los flags GoldSrc.
8. Left/Right se interpreta desde FacingOrientation, mirando hacia la puerta. Una inferencia ambigua, hoja cerrada bloqueada o movimiento incierto produce omisión conservando el vano. Se conservan marcos identificados. Las envolventes conservadoras pueden rechazar movimientos que requerirían una comprobación más precisa.
9. Añade Light, Survivor, Weapon, Medkit o Checkpoint mediante puntos Revit o edición manual. X/Y/Z están en pies internos; Yaw está en grados. Survivor usa posición de los pies y Order 1–4 sin duplicados; LightColor usa `R G B intensidad`. Weapon genera una pistola inicial; Medkit genera un botiquín.
10. La búsqueda automática de supervivientes usa pisos exportados, hull de 32 × 32 × 72 unidades y separación comprobada; tiene presupuesto de 10 s / 25.000 candidatos. Si falla, añade cuatro posiciones manuales. Comprueba también su ubicación jugable: la geometría no permite inferir qué habitación deseas usar.
11. Guarda/carga el JSON lateral para conservar configuraciones. Se verifica documento y versión de esquema; la exportación no modifica parámetros, familias ni RVT. Los parámetros GoldSrc existentes no controlan las puertas Source.
12. **Analizar** no publica archivos. **Exportar VMF** publica geometría, materiales, configuración y reporte. **Compilar BSP** se habilita tras una publicación válida.
13. **Escena de prueba** genera la habitación C# de referencia, independientemente del RVT abierto. No reemplaza el modelo Revit.

Las opciones y el reporte tienen desplazamiento separado; los seis botones de acción quedan fuera del área desplazable. Se comprobó el contenedor WPF con 1.000 diagnósticos al 100 %, 150 % y 200 %, hasta 560 × 360 DIP. Esto no sustituye una revisión de la ventana real en Revit.

## Puertas de refugio

Checkpoint utiliza `prop_door_rotating_checkpoint`, no una puerta GoldSrc. Sus modelos y texturas se resuelven en los VPK del juego instalado, sin copiarlos al paquete.

- Selecciona el **pivote del modelo**, no necesariamente el borde inferior del vano; ajusta su Z y Yaw.
- Introduce OpeningWidthFeet y OpeningHeightFeet correspondientes al vano. Se comparan con los límites conservadores del modelo, sin deformarlo ni escalarlo. Una incompatibilidad se registra como omisión.
- El movimiento usa apertura fija de 90° y comprueba posición cerrada/arco contra arquitectura y puertas cerradas.
- `models/props_doors/checkpoint_door_01.mdl` existe en esta instalación; su hull es aproximadamente Min `(-6.7134,-7.8254,-52.3666)` / Max `(3.0637,60.5470,52.6334)` unidades Source. Con escala 12, el ancho conservador es 5,6977 pies y la altura 8,75 pies. El pivote está dentro de la altura del modelo; no lo coloques sobre el suelo sin corregir Z.
- Los límites incluyen salientes y pueden impedir el uso de un modelo en un vano más pequeño. No se asume compatibilidad con todas las familias de puertas Revit.
- Una puerta checkpoint no crea por sí sola una campaña/refugio funcional: atributos de navegación, flujo y transiciones requieren diseño y validación posteriores.

Consulta los límites de otro modelo oficial instalado:

```powershell
dotnet run --project Revit2Source.Sample -- --checkpoint-bounds models/props_doors/checkpoint_door_01.mdl
```

## Materiales y publicación

Los colores e imágenes se convierten a VTF 7.2 BGRA8888 opaco, con mipmaps completos y dimensiones potencia de dos de hasta 256 píxeles. VMT utiliza LightmappedGeneric. No hay paleta WAD ni WAD de Source.

Se conserva el tamaño físico, rotación y desplazamiento disponibles mediante ejes VMF. La colocación exacta puede diferir de Revit. Imágenes ausentes usan color del material y registran la causa; cobertura ambigua e interiores de descomposición usan gris neutro. Los identificadores de material incluyen documento; imágenes equivalentes se deduplican. Reflejos, relieve y transparencias complejas están fuera de esta versión.

La exportación publica:

- `nombre.vmf`, `nombre_source_report.json` y `nombre_source_config.json`.
- `materials/revit2source/<nombre>/<hash>.vmt` y `.vtf`.
- `nombre_nav_readme.txt` y `cfg/nombre_nav_review.cfg`.
- Después de compilar: `nombre.bsp` y `nombre_source_compile.json`.

La publicación restaura archivos anteriores si falla. No elimina los recursos de exportaciones previas. Los materiales propios deben acompañar el BSP: el compilador no los empaqueta automáticamente dentro de su pakfile. VPK queda pendiente.

## Compilación y prueba local

Se detectan Steam y sus bibliotecas. La carpeta editable debe contener `bin` y `left4dead2`; se comprueban SteamAppId 550, FGD y rutas de compiladores de esa instalación.

La compilación crea `.Revit2Source-build/<fecha_guid>/`, copia materiales propios y deriva gameinfo.txt preservando montajes de juego/DLC con rutas absolutas. Ejecuta VBSP, VVIS y VRAD con procesos sin shell, argumentos separados, salida capturada y cancelación. No escribe en la instalación ni inicia el juego. Logs y resultados de etapas quedan en el directorio de trabajo, incluso cuando falla.

Leaks, errores de proceso, límites excedidos y BSP incompleto impiden publicar el resultado. Se comprueban cabecera versión 21, rangos de lumps, modelos, supervivientes, Director, VIS e iluminación. El SDK público Source 2013 se utiliza como referencia, no como prueba de compatibilidad con L4D2.

Esta instalación imprime `Cannot build Physics2 data` en VBSP y devuelve éxito; conserva la colisión física clásica en el BSP. El mensaje se recoge entre las advertencias. La colisión efectiva continúa pendiente de comprobación en el juego.

Desde consola, sin Revit ni Hammer:

```powershell
dotnet run --project Revit2Source.Sample -c Release -- --sample Examples/Source/revit_l4d2_room.vmf --compile
dotnet run --project Revit2Source.Sample -c Release -- --compile Examples/Source/revit_l4d2_room.vmf
```

Para probar **manualmente**, copia el BSP a `left4dead2/maps` y el árbol `materials/revit2source` a `left4dead2/materials/revit2source`. Los modelos oficiales ya pertenecen al juego instalado. Después, en la consola de L4D2:

```text
map revit_l4d2_room coop
sv_cheats 1
nav_edit 1
```

Marca superficies caminables cuando corresponda con `nav_mark_walkable`, ejecuta `nav_generate` y revisa el resultado tras la posible recarga. Después ejecuta `nav_analyze`, `nav_trouble_report` y `nav_save`, por separado. Comprueba conexiones, áreas huérfanas, escaleras y paso de puertas; vuelve a generar/revisar después de cambios geométricos.

Esta habitación no tiene flujo completo de campaña ni transición. PLAYER_START/CHECKPOINT, zonas de aparición, recorrido inicial/final y comportamiento del Director necesitan diseño adicional y prueba manual. La presencia de info_director y un BSP correcto no prueba generación de infectados. No se ejecutan automáticamente los comandos ni se inicia Half-Life/L4D2.

## Desarrollo y límites

`Revit2Source` y `Revit2Source.Compilation` son netstandard2.0 y no referencian GoldSrc. El adaptador mantiene net48 para Revit 2024, net8.0-windows para 2025/2026 y net10.0-windows para 2027. Solo 2024/2025 están compilados contra instalaciones locales.

El perfil geométrico Source permite hasta 64 vértices de cara y mantiene 128 caras / 4096 piezas por sólido. Las caras mayores se dividen antes de serializar, conservando volumen; una prueba nativa con 126 vértices provocó fallo de VBSP, mientras 64 y la versión dividida compilaron. El perfil GoldSrc predeterminado mantiene su división por 31 vértices de cara, evitando cambiar resultados existentes. Se validan límites globales conservadores y coordenadas ±16384. Las omisiones no se sustituyen por cajas envolventes.

MEP complejo, vínculos en la nueva ventana, recuperación de las 326 omisiones, campañas completas, Survival, exportación MDL propia y VPK están pendientes. Las superficies curvas solo se aproximan si el lector y las comprobaciones actuales pueden verificarlas. La llamada nativa de teselación Revit no puede interrumpirse mientras la API está ejecutándola.

```powershell
dotnet test Revit2GoldSrc.Tests -c Release
./scripts/Build.ps1 -Versions 2024,2025
./scripts/Install.ps1 -Version 2024
```

Las pruebas de herramientas oficiales se omiten explícitamente si L4D2 Authoring Tools no está disponible; las pruebas geométricas y de formatos siguen ejecutándose. No redistribuimos FGD, modelos ni imágenes originales del juego.
