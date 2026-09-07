# Lib.mesh.modify

## Operators

- [**BlendMeshToPoints**](BlendMeshToPoints.md) — Allows to deform a mesh with a point buffer, for example you can use [MeshVerticesToPoints] and apply a point modifier.
- [**BlendMeshVertices**](BlendMeshVertices.md) — Blends between two sets of mesh vertices. This only yields meaningful (i.e., predictable) results for meshes with the same vertex count and topology.
- [**CollapseVertices**](CollapseVertices.md) — A simple effect that snaps the position of vertices to a grid.
- [**ColorVerticesWithField**](ColorVerticesWithField.md) — Uses a color field to set vertex colors from their position in that field.
- [**CombineMeshes**](CombineMeshes.md) — Combines the connected mesh buffers into a new mesh
- [**CustomFaceShader**](CustomFaceShader.md) — Uses shader and some parameters to manipulate mesh faces.
- [**CustomVertexShader**](CustomVertexShader.md) — Uses custom shader code to manipulate the vertices of a mesh.
- [**DeformMesh**](DeformMesh.md) — Spherize, Taper and Twist. It works better if your mesh has a high density of vertices.
- [**DisplaceMesh**](DisplaceMesh.md) — Distorts the input mesh by displacing its vertices by an amount controlled by the connected input texture.
- [**DisplaceMeshNoise**](DisplaceMeshNoise.md) — Displaces a Mesh with Perlin noise.
- [**DisplaceMeshVAT**](DisplaceMeshVAT.md) — VAT stands for Vertex Animation Texture.
- [**FlipNormals**](FlipNormals.md) — Reverses the normals of the input mesh. This can be useful for drawing shader "inside" surfaces of a mesh.
- [**MeshFacesPoints**](MeshFacesPoints.md)
- [**MeshProjectUV**](MeshProjectUV.md) — Overwrites the existing UV coordinates with planar mapping.
- [**MoveMeshToPointLine**](MoveMeshToPointLine.md) — Deforms a mesh in world space origin along a line defined by points.
- [**PickMeshBuffer**](PickMeshBuffer.md) — Switch between different mesh buffers
- [**RecomputeNormals**](RecomputeNormals.md) — Recalculates the normals (smoothing groups) of the surfaces of a mesh. For example, if these are changed by [DeformMesh] and similar operators
- [**ScatterMeshFaces**](ScatterMeshFaces.md) — This op is similar to [DisplaceMesh]. However, it uses face centers instead of the vertices to "scatter" a mesh while keeping the size of the individual faces.
- [**SelectVertices**](SelectVertices.md) — Sets the selection property of mesh vertices from a volume. This can later be used to selectively apply manipulations like displace.
- [**SelectVerticesWithSDF**](SelectVerticesWithSDF.md) — Allows selecting and adding values to vertices in 3D space via SDF Objects/Fields.
- [**SplitMeshVertices**](SplitMeshVertices.md) — This can be relevant to "scatter" smooth shaded geometry or change shading to flat.
- [**TextureDisplaceMesh**](TextureDisplaceMesh.md) — Uses a projected texture with a normal map to displace the mesh.
- [**TransformMesh**](TransformMesh.md) — Generates a new set of transformed vertices for a mesh.
- [**TransformMeshUVs**](TransformMeshUVs.md) — Manipulates the existing UV coordinates of a mesh to change the mapping of textures.
- [**Warp2dMesh**](Warp2dMesh.md) — Uses a set of reference points to warp a mesh geometry. This can be useful for adjusting projection mapping.

---

*Auto-generated from the operator library.*
