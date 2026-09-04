# Lib.mesh.draw

## Operators

- [**DrawMesh**](DrawMesh.md) — Uses PBR rendering to draw incoming geometry and meshnodes according to the desired settings.
- [**DrawMeshAtPoints**](DrawMeshAtPoints.md) — Draws PBR shaded instances of a mesh defined by the connected point buffer.
- [**DrawMeshCelShading**](DrawMeshCelShading.md) — This is an attempt to create a cel shader. In fact, it's more a gradient-based shading as you need to use a gradient to define what colors will be used to shade your mesh. Make sure to use at least one [PointLight] and adjust the Brightness and the light intensity. You can set the gradient interpolation to hold, but you can still use the other interpolations; then you'll have full control, for example, if you don't want hard edges between colors.
- [**DrawMeshChunksAtPoints**](DrawMeshChunksAtPoints.md)
- [**DrawMeshHatched**](DrawMeshHatched.md) — Draws incoming geometry and meshnodes in an abstract shading style.
- [**DrawMeshUnlit**](DrawMeshUnlit.md) — Draws incoming geometry and meshnodes without any shading and according to the desired settings.
- [**DrawMeshWithShadow**](DrawMeshWithShadow.md) — Is used together with [SetShadow] and a [SetPointLight] to render incoming geometry with a shadow map.
- [**VisualizeUvMap**](VisualizeUvMap.md) — A draw helper to check the UV maps of a given mesh

---

*Auto-generated from the operator library.*
