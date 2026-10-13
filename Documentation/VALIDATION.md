# Validación del MVP

## Comprobaciones automatizadas ejecutadas

- Compilación Release contra Revit 2024 (.NET Framework 4.8): correcta, sin advertencias.
- Compilación Release contra Revit 2025 (.NET 8 Windows): correcta, dos advertencias MSB3277 de dependencias Autodesk.
- Pruebas en .NET Framework 4.8, .NET 8 y .NET 10: correctas. Consulte la salida de `dotnet test` para el número actualizado.
- Mapa de ejemplo generado por `Exporter`, releído y validado: 13 brushes, 78 caras, 3 entidades.
- Rechazo comprobado de DLL 2025 al solicitar 2024.
- Rechazo comprobado de compilaciones 2026/2027 cuando faltan sus DLL; no se emiten paquetes alternativos con DLL de otro año.
- No se incluyen `RevitAPI.dll` ni `RevitAPIUI.dll` en los paquetes.
- 152 casos por runtime: 456 ejecuciones correctas en la última verificación. Incluyen recomendaciones de texturas, diagnóstico JSON/reproducción de contornos y motivos de fallo de spawn.
- Paquete 2024 instalado para el usuario actual, con manifiesto y DLL verificados. Esto confirma los archivos de instalación, no la carga dentro de Revit.

La convención de planos sigue `PlaneFromPoints` del [compilador GoldSrc de Valve](https://github.com/ValveSoftware/halflife/blob/master/utils/qcsg/brush.c): producto cruzado `(p0-p1) × (p2-p1)` y semiespacios interiores detrás de los planos. El fixture `reference_cube.map` está escrito independientemente del generador. El hull de jugador usa las dimensiones indicadas en esa misma fuente.

## Comprobación manual pendiente, por versión

1. Cerrar Revit, instalar el paquete del año y abrirlo de nuevo. Verificar pestaña, botón y ventana modal.
2. Crear una habitación con interior de 4 × 5 m, altura 3 m, muros básicos de 0.15 m y losa horizontal de 0.20 m. Dejar extremos rectangulares verificables; las uniones diagonales se omiten.
3. Exportar todo y repetir seleccionando únicamente un muro y la losa. Confirmar que el modelo no cambia ni aparece una transacción de edición.
4. Añadir puerta de 0.90 × 2.10 m desplazada del centro. Revisar dimensiones reales de la abertura en el sólido, no solo los parámetros nominales de la familia.
5. Añadir ventana con antepecho, girar un muro 45° y colocar columna rectangular girada/reflejada. Confirmar huecos, giros y posiciones en el editor.
6. Ensayar losa rectangular rotada, una instancia anidada rectangular y un elemento desplazado lejos del origen. Verificar dimensiones y centrado.
7. Incluir un muro curvo, losa con hueco y un vínculo seleccionado: deben quedar omitidos con identificadores y motivos en la ventana/reporte.
8. Revisar WAD faltante, texturas sin asignar, salida no escribible, cancelación y confirmación de reemplazo.
9. Abrir el mapa en J.A.C.K. / Hammer GoldSrc, comprobar problemas y compilar con herramientas que acepten coordenadas decimales. Las herramientas no se encontraron en PATH ni en la instalación local examinada de Half-Life durante el desarrollo inicial.
10. Jugar el BSP y verificar spawn, colisiones y paso por la puerta. Confirmar límites y posibles diferencias numéricas con el compilador elegido.

Repetir en 2024 y 2025. Para 2026 y 2027 se requieren además sus DLL y runtimes anfitriones antes de declarar compatibilidad validada.

## Actualización de diagnóstico y texturas

- Comprobar que el botón de recomendaciones está desactivado hasta cargar un WAD válido.
- Probar «Solo campos vacíos», «Reemplazar todas», Escape, X y «Cancelar». Verificar las cuatro asignaciones y la conservación de selecciones manuales.
- Cambiar de WAD: las selecciones deben vaciarse. Un intento de cargar un WAD inválido conserva el catálogo anterior válido.
- Exportar `MODELO CB.rvt` con el nuevo nombre predeterminado y revisar los contornos en `Geometry`, códigos e índices del reporte.
- Si no hay elementos compatibles, verificar `MapPublished=false`, la existencia del reporte y que no se reemplazó un mapa anterior.
- Confirmar en la ventana los motivos diferenciados de fallo de jugador.

El nuevo diagnóstico sobre los 11 elementos reales requiere volver a exportar desde Revit. Las pruebas automatizadas utilizan contornos sintéticos y no demuestran que esos elementos ya puedan convertirse.

## Comprobación manual pendiente de materiales

1. Usar una losa con bitmap de baldosa de tamaño conocido, un muro de color y una cara pintada con otro material.
2. Exportar con **Materiales de Revit (WAD propio)** y revisar los tres archivos y `Materials` del reporte.
3. Repetir con escala 12 y 24 HU/pie y con un muro rotado: el tamaño físico de repetición debe mantenerse.
4. Mover temporalmente una imagen o usar un recurso procedural: comprobar el color sustituto y su advertencia sin alterar la geometría.
5. Abrir el MAP en J.A.C.K., cargar su WAD propio y comparar colores, patrones y orientación con Revit. Compilar y jugar el BSP si dispone de compiladores compatibles.

El test de cara pintada simula la asignación extraída; todavía requiere confirmar la consulta de pintura real en Revit. Los esquemas de apariencia no reconocidos se sustituyen por color. Las posiciones exactas y los efectos de renderizado no se consideran equivalentes al render de Revit.

## Puertas y VHLT

La ampliación se detalla en [Doors.md](Doors.md). La escena C# de dos puertas se compiló con los cuatro VHLT instalados en J.A.C.K.; BSP30 validado con 3 modelos, 5 entidades y bisagra correcta. La prueba de fuga conserva el BSP anterior. La ejecución de los nuevos comandos en Revit y la prueba jugable en Half-Life están pendientes.

## Recuperación poligonal e interfaz

Los 11 sólidos del reporte del modelo se recuperaron conservando volúmenes y límites; el MAP reconstruido combina 19/19 elementos, con 41 brushes. Se compiló su variante con recinto mediante VHLT. Se registró el WAD en J.A.C.K. con backup después de cerrar el editor. Los recursos de iconos fueron renderizados en ambos temas a 100/150/200%; la revisión dentro de Revit y la nueva exportación real están pendientes. Detalles en [GeometryRecovery.md](GeometryRecovery.md).

## Familias, MEP y modelos vinculados

La escena mixta C# se compiló con VHLT y produjo BSP30 con decoración sin colisión. Las pruebas cubren mallas cerradas, tuberías huecas, codos, equipos cóncavos/inclinados y materiales de documentos distintos. La extracción del nuevo perfil dentro de Revit con vínculos reales sigue pendiente. Consulte [Families.md](Families.md).

Corrección de interfaz tras análisis extenso (2026-10-13): el contenedor principal usa filas Grid con altura proporcional para opciones y reporte. Cada área tiene desplazamiento independiente y un separador ajustable; la fila de botones queda fuera de las áreas desplazables y se distribuye en varias filas cuando falta ancho. El tamaño inicial respeta el área de trabajo y la altura mínima se reduce a 360 DIP. Se mantiene el comportamiento y los estados de las acciones.

Verificación WPF aislada: scripts/VerifyExportLayout.ps1, 1000 diagnósticos, seis combinaciones de tamaño y escalado 100/150/200 %, incluyendo 560 x 360 DIP. Se comprobaron límites de todos los botones, área útil de opciones/reporte y desplazamiento del texto; renders en artifacts/layout-preview, inspeccionado el caso más pequeño al 200 %. Compilación y paquetes Revit 2024/2025 correctos (dos advertencias Autodesk conocidas en 2025). Pendiente revisión en la ventana real dentro de Revit; no se modificó la conversión geométrica ni las 326 omisiones del análisis mostrado por el usuario.


## Source / L4D2 — 2026-10-13

Se añadieron proyectos Source independientes y ventana/panel Revit; los lectores compartidos conservan valores predeterminados GoldSrc. 197 pruebas/runtime (591 correctas) en Release net48/net8/net10, incluidas compilaciones oficiales, rechazo de leaks, cancelación y conservación de BSP anterior. Se inspeccionaron el FGD instalado y ejemplos oficiales del SDK.

`Examples/Source/revit_l4d2_room.vmf` es escena sintética C# de 4 × 5 m, no un RVT exportado: 9 elementos, 10 sólidos, 60 caras, 18 entidades; 4 supervivientes, puerta, 4 pistolas, 4 botiquines, 2 luces. Compilación real VBSP/VVIS/VRAD, BSP21 con 2 modelos, VIS e iluminación. Materiales VMT/VTF separados; no asumir que están empaquetados en el BSP. VBSP imprime aviso Physics2, con colisión clásica presente; ejecución jugable aún pendiente.

WPF aislado con seis acciones, 1000 diagnósticos, seis tamaños/escalados hasta 560 × 360 DIP al 200 %. Pendiente: ventana/extracción real Revit, carga visual L4D2, Nav Mesh, bots, recursos, animación y Director. No se inició ningún juego ni se copió contenido a su instalación.
