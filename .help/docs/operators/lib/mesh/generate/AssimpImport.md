# AssimpImport

*in [Lib.mesh.generate](README.md)*

Loads a 3D model file with Assimp as mesh, points, polygon geometry and skeleton with animations.

Reads 40+ formats, among them FBX, glTF/GLB, OBJ, Collada (DAE), STL, PLY, 3DS, Blend, X3D and 3MF. All meshes of the file are combined with their node transforms applied; each mesh instance becomes one chunk of MeshData and one part of Geometry. Loading runs in the background, in a separate process when possible, so large files don't stall playback and a broken file can't crash TiXL; the previous model stays visible until the new one is ready, and the file hot-reloads when it changes on disk.

Outputs:
- MeshData: triangulated mesh for [DrawMesh] and the other mesh operators.
- Geometry: polygonal geometry with N-gons preserved (quads stay quads) for [BevelGeometry], [ExplodeGeometry], [GeometryToMesh] and the other geometry operators.
- Points: one point per vertex, with the vertex color.
- Setup: materials, skeleton and animation clips for [DrawScene], [SampleGltfAnimation] and [PoseToSkinMatrices]. [DrawScene] draws each part with its own material.
- Material: the material of the first mesh, e.g. for [UseMaterial] around a [DrawMesh].
- SkinWeights / SkeletonPoints: for [SkinMesh], [SkinPoints] and visualizing the rig.

Materials are converted to PBR materials with base color, emissive, normal and roughness/metallic/occlusion maps. Textures are read from the file (embedded) or from image files next to the model, also when the file stores an absolute path of another machine.

AKA: fbx, gltf, glb, dae, collada, 3ds, stl, ply, blend, model, import, assimp

See also: [LoadObj], [LoadGltfScene], [LoadObjGeometry], [AssimpExport], [AssimpImportExample] (Examples > mesh)

Tips:
- Normalize fits any model into a unit cube around the origin, which helps with files in centimeters or kilometers.
- Use YUpToZUp / ZUpToYUp for files authored with a different up axis. Both are rotations, so animations stay intact; the Flip modes mirror the model and reverse the triangle winding to keep faces pointing outward.
- Files larger than 2 GB are rejected, and one import keeps at most 1 GiB of decoded textures; textures that don't fit are skipped and listed in the status.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **FlipNormals** (Boolean) | Inverts all normals and reverses the triangle winding, for models that render inside out. |
| **SelectedMeshIndex** (Int32) | Imports only the mesh with this index (see MeshNames). -1 imports all meshes. |
| **TriggerReload** (Boolean) | Reloads the file, e.g. after changing files it references. |
| **PostProcessFlags** (Int32) | How much Assimp prepares the mesh.<br/><br/>None: only triangulation and validation; missing normals are computed from the faces.<br/>Basic: generates smooth normals where the file has none.<br/>Full: also generates UVs, tangents, joins identical vertices and optimizes the vertex cache order. |
| **Scale** (Single) | Uniform scale, applied after normalization. |
| **Normalize** (Boolean) | Centers the model at the origin and scales it to fit into a unit cube. |
| **AxisConversionMode** (Int32) | Converts between coordinate conventions. YUpToZUp and ZUpToYUp rotate the model by 90 degrees around X; the Flip modes mirror it along one axis. |
| **ImportMaterials** (Boolean) | Reads materials and decodes their textures. Turn off for faster loading when the model gets its own material. |
| **FilePath** (String Required) | Path to the model file. |
| **IsolateImport** (Boolean) | Imports in a second TiXL process (started with --assimp-host), so a crash or hang inside Assimp only ends that process and shows an error. When turned off, the import runs inside TiXL. Metadata says which one was used. |

## Outputs
| Name | Type |
|---|---|
| **MeshData** | T3.Core.DataTypes.MeshBuffers |
| **Geometry** | T3.Core.DataTypes.MeshGeometry |
| **Points** | T3.Core.DataTypes.BufferWithViews |
| **Setup** | T3.Core.DataTypes.SceneSetup |
| **Material** | T3.Core.Rendering.Material.PbrMaterial |
| **SkinWeights** | T3.Core.DataTypes.BufferWithViews |
| **SkeletonPoints** | T3.Core.DataTypes.BufferWithViews |
| **Metadata** | System.String |
| **VertexCount** | System.Int32 |
| **FaceCount** | System.Int32 |
| **MeshCount** | System.Int32 |
| **BoundsMin** | System.Numerics.Vector3 |
| **BoundsMax** | System.Numerics.Vector3 |
| **MeshNames** | System.String |
| **BoneCount** | System.Int32 |
| **AnimationCount** | System.Int32 |
| **AnimationNames** | System.String |

