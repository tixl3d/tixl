# MeshToGeometryExample

*in [Lib.physics](README.md)*

[MeshToGeometry] reads a GPU mesh back as geometry, so meshes from [TorusMesh], [IcosahedronMesh], [LoadGltf] and every other mesh operator can collide. The readback only happens when the mesh changes.

Each mesh becomes a body at every scattered point. They are drawn with their original mesh through [RigidBodyPoints] and [RepeatMeshAtPoints], so smooth normals and UVs stay as they were. The tori are concave and keep their holes.

AKA: mesh collider, mesh readback, gltf physics, import, cpu mesh

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- A mesh with several chunks, such as a loaded model, arrives as several parts: use Pieces to choose between one body per part and one for the whole model.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


