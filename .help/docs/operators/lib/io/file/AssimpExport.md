# AssimpExport

*in [Lib.io.file](README.md)*

Writes a mesh, a polygon geometry or a point cloud to a 3D file with Assimp.

Supports OBJ, FBX, glTF/GLB, STL, PLY, Collada (DAE), 3DS, 3MF, X3D and every other exporter listed in SupportedFormats. ExportMesh writes Geometry if it is connected (keeping N-gons for formats that store them, one object per geometry part), otherwise MeshData. ExportPoints writes Points as a point cloud. Files are numbered MeshName_001.ext, MeshName_002.ext and so on; existing files are never overwritten. The file is written in the background, in a separate process when possible, so playback continues and a crashing native exporter can't take the editor down.

AKA: save, write, fbx, gltf, glb, stl, ply, 3d print, model, assimp

See also: [WriteObj], [AssimpImport], [AssimpImportExample]

Tips:
- A relative FolderPath writes into the assets of the current project, e.g. "exports"; prefix a project name ("MyProject:exports") or use an absolute path to write elsewhere.
- For 3D printing use Stl or ThreeMf; for other DCC tools Fbx, Glb or Obj.
- GlTF and OBJ exports may write sidecar files (.bin, .mtl) named after the main file; the numbering covers only the main file.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Geometry** (MeshGeometry) | Polygon geometry to write with ExportMesh. Takes precedence over MeshData. |
| **ExportMesh** (Boolean) | Writes Geometry or MeshData to the next numbered file. |
| **MeshName** (String) | Base name of the files and of the exported object. With ExportFormat Auto, its extension (e.g. "chair.glb") picks the format. |
| **ResetCounter** (Boolean) | Restarts the file numbering at 001. Existing files are still skipped. |
| **IsolateExport** (Boolean) | Exports in a second TiXL process (started with --assimp-host), so a crash or hang inside an Assimp exporter only ends that process and shows an error. When turned off, the export runs inside TiXL; the status says which one was used. |
| **MeshData** (MeshBuffers) | Mesh to write with ExportMesh. It is read back from the GPU when the export is triggered. |
| **ExportPoints** (Boolean) | Writes Points to the next numbered file. |
| **Points** (BufferWithViews) | Points to write as point cloud with ExportPoints. Separator points are skipped. |
| **ExportFormat** (Int32) | File format. Auto derives it from the extension of MeshName and falls back to Obj. Custom uses the format id in CustomFormat. PlyBinary may not be readable by every Assimp version; Ply writes ASCII. |
| **CustomFormat** (String) | Assimp format id used when ExportFormat is Custom, e.g. "fbxa" or "assjson". See the SupportedFormats output. |
| **FolderPath** (String) | Target folder. Relative folders are created inside the current project's assets. |

## Outputs
| Name | Type |
|---|---|
| **Success** | System.Boolean |
| **StatusMessage** | System.String |
| **LastFilePath** | System.String |
| **SupportedFormats** | System.String |

