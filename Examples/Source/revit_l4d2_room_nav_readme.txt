L4D2: prueba local, no campaña completa.
Copie manualmente el BSP a left4dead2/maps y los materiales a left4dead2/materials, o use un directorio de contenido montado.
En consola: map revit_l4d2_room coop
sv_cheats 1
nav_edit 1
nav_mark_walkable sobre superficies caminables; después nav_generate. Puede recargar el nivel.
Revise conexiones, escaleras, paso de puertas y áreas huérfanas.
nav_analyze
nav_trouble_report
nav_save
La habitación no define flujo completo de campaña. Las anotaciones PLAYER_START/CHECKPOINT, zonas de aparición y flujo requieren un recorrido inicial/final diseñado y revisión manual.
Repita navegación tras modificar arquitectura. Pruebe humanos, bots, apertura, colisiones y Director.
No se declara funcionamiento de infectados ni Director antes de esta validación. No redistribuya modelos/texturas de Valve.
