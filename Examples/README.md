# Habitación de ejemplo

`room.map` contiene una habitación interior de 4 × 5 m y 3 m de altura, una puerta abierta de 0.90 × 2.10 m, suelo, recinto exterior cerrado, jugador y luz. Tiene 13 brushes, 78 caras y 3 entidades.

`test.wad` contiene una textura TEST original de 16 × 16 píxeles, creada para verificar este proyecto. No contiene recursos de Valve. Es un recurso de prueba; el modo de WAD existente utiliza este recurso; el modo de materiales genera un WAD propio.

Regenerar desde la raíz del repositorio, usando el mismo exportador que el plugin:

```powershell
dotnet run --project Revit2GoldSrc.Sample -- Examples/test.wad Examples/room.map
```

También puede pasar un WAD propio; la herramienta de ejemplo elige la primera textura sólida normal. El plugin exige elegirlas explícitamente.

La ruta WAD y la ruta del reporte son absolutas y corresponden al equipo donde se generó el ejemplo. Tras mover el repositorio, regenere el ejemplo o ajuste la propiedad `wad` en J.A.C.K.

Este mapa se ha validado mediante el lector del proyecto. Su apertura en J.A.C.K., compilación a BSP y ejecución en Half-Life requieren comprobación manual.

## Ejemplo del WAD de materiales

```powershell
dotnet run --project Revit2GoldSrc.Sample -- --materials Examples/material_room.map
```

Genera `material_room.map`, `material_room_materiales.wad` y su reporte con el mismo pipeline del complemento. La baldosa y la pintura beige son recursos sintéticos originales para comprobar el flujo; no son una exportación de materiales reales de Revit ni una validación visual en J.A.C.K.

## Puertas animadas

`doors_demo.map`, `doors_demo.wad`, `doors_demo_tools.wad`, `doors_demo_export_report.json` y `doors_demo.bsp`: escena cerrada generada por `Sample --compile-doors`. Texturas de color propias; BSP30 con mundo y dos modelos dinámicos. Consulte [Doors.md](../Documentation/Doors.md). Para mover el MAP, actualice sus rutas WAD o vuelva a generarlo en el destino. El BSP incorpora las texturas y no requiere estos WAD en ejecución.

## Familias y MEP

`families_demo.map`, `families_demo_materiales.wad`, `families_demo_export_report.json` y `families_demo.bsp` forman una escena mixta generada por C#: tuberías, codo, equipo, mobiliario sin colisión y geometría con vínculo simulado. BSP30 compilado mediante VHLT. Consulte [Families.md](../Documentation/Families.md).
