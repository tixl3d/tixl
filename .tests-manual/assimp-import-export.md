---
id: assimp-import-export
title: Assimp Import and Export
scope: mesh
tags: [regression, cross-platform]
added: 2026-11-09
added-in-version: 4.4
prerequisites:
  - An empty project is open.
  - A few model files are copied into the project's assets folder, e.g. an .fbx with a skinned animated character, a multi-object .glb (Examples:test-footage/gltf/gltf-scene-test-01.glb), an .obj with quads and a corrupt file (any text file renamed to broken.fbx).
related-help:
  - ../.help/docs/using/ProceduralGeometry.md
  - ../.help/docs/using/CharacterAnimation.md
---

Verifies [AssimpImport] and [AssimpExport]: background loading, the mesh, geometry and skinning
outputs, error handling and file export. Run it on Windows, Linux and macOS.

## Step: Loading a model

**Action:**
Add `[AssimpImport]` → `[DrawMesh]`, pin the `[DrawMesh]` output and set `FilePath` to the multi-object .glb.

**Expected:**
- The model appears without a visible hitch of the playback; the status briefly shows "Loading ...".
- All three objects are visible at their correct positions (node transforms applied).
- The faces point outward (not inside-out) and are lit correctly.
- `MeshCount` is 3 and `Metadata` lists vertex, triangle and polygon counts.

## Step: Materials

**Action:**
Load a textured model (e.g. Examples:meshes/DamagedHelmet.glb and an .obj with an .mtl and image files next to it). Connect `Setup` → `[DrawScene]`.

**Expected:**
- Base color, normal, emissive and roughness/metallic maps show up like with `[LoadGltfScene]`.
- Multi-material files draw each part with its own material.
- Changing `Scale` reloads quickly without decoding the textures again.
- Editing a texture file next to the model and clicking `TriggerReload` shows the new texture.
- With `ImportMaterials` off, `[DrawScene]` uses the material of the context and loading is faster.
- A missing texture file shows a warning naming it; the model still loads.

## Step: Normalize, scale and axis conversion

**Action:**
Toggle `Normalize`, change `Scale` to 2, then step `AxisConversionMode` through all modes.

**Expected:**
- With `Normalize` on, the model fits into a unit cube around the origin; `BoundsMin`/`BoundsMax` reflect it.
- `YUpToZUp` and `ZUpToYUp` rotate the model by 90 degrees; the Flip modes mirror it.
- Faces stay outward-facing in every mode, including the mirrored ones.
- Each change reloads in the background; the previous model stays visible until the new one is ready.

## Step: Geometry output

**Action:**
Load the .obj with quads. Connect `Geometry` → `[BevelGeometry]` → `[GeometryToMesh]` → `[DrawMesh]`.

**Expected:**
- The bevel follows the quads (no diagonal bevels across faces).
- Edges at UV seams bevel like any other edge (no cracks).
- With the multi-object .glb, `[ExplodeGeometry]` moves each object separately.

## Step: Skinned animation

**Action:**
Load the animated .fbx. Connect `Setup` → `[SampleGltfAnimation]` and `[PoseToSkinMatrices]` (Setup inputs), `[SampleGltfAnimation]` Pose → `[PoseToSkinMatrices]` Pose, then `MeshData`, `SkinWeights` and `SkinMatrices` → `[SkinMesh]` → `[DrawMesh]`. Start playback.

**Expected:**
- The character animates; at time 0 without a pose it matches the static `MeshData`.
- `AnimationNames` lists the clips; changing `ClipIndex` switches between them.
- With `Normalize` and `YUpToZUp` enabled the animation still plays correctly, just scaled and rotated.

## Step: Errors and hot reload

**Action:**
Set `FilePath` to broken.fbx, then to a file name that doesn't exist, then back to the .glb. Afterwards overwrite the .glb in the assets folder with another model file.

**Expected:**
- broken.fbx shows an error status with the Assimp message; the editor keeps running and no previous model is shown.
- The missing file shows "File not found".
- Switching back loads the model again.
- Overwriting the file reloads it automatically.
- A file larger than 2 GB is rejected with "File is too large" before it can exhaust memory.

## Step: Isolated import

**Action:**
Load the .glb and read the last lines of `Metadata`. Then toggle `IsolateImport` off and on, and change `FilePath` to a large file and back while it is still loading.

**Expected:**
- `Metadata` says "Imported in a separate process" (a second TiXL process, no .NET installation required).
- Toggling `IsolateImport` reloads immediately; with it off, `Metadata` says "Imported in-process".
- Results look identical with `IsolateImport` on and off.
- Switching the file while loading stops the previous import; no `dotnet` process stays behind (check the task manager / `ps`).
- Closing TiXL during a long import ends the import process too.

## Step: Exporting

**Action:**
Add `[AssimpExport]`. Connect the `Geometry` of the .glb import, set `ExportFormat` to Glb and click `ExportMesh` twice. Then connect `MeshData` instead (disconnect Geometry), choose Stl and export. Finally connect `Points`, choose Ply and click `ExportPoints`.

**Expected:**
- `mesh_001.glb` and `mesh_002.glb` appear in the project's `exports` asset folder; the status shows each file name and "(separate process)".
- Loading `mesh_001.glb` with a second `[AssimpImport]` shows the same three objects.
- The STL and PLY files are written; exporting points with Stl shows an error that the format can't store point clouds.
- `ResetCounter` restarts numbering, but existing files are skipped instead of overwritten.
- Triggering an export while one is still running reports that the previous export is still running.

## Step: Isolated export

**Action:**
Turn `IsolateExport` off and export once, then on again. Point `FolderPath` at a folder that can't be written to and trigger another export.

**Expected:**
- With isolation off, the status ends with "(in-process)"; with it on, "(separate process)". Both produce the same file.
- A failed export shows an error and leaves no partial file behind at the target path.
- No `dotnet` process stays running after either export.
