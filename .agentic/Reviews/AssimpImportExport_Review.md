# Review: [AssimpImport] / [AssimpExport]

Port of the Windows-era `feat/assimp` operators onto the cross-platform code base, reviewed with
the four lenses of [FEATURE_REVIEW.md](FEATURE_REVIEW.md). Part 1 lists what was wrong in the
original branch and how the port resolves it. Part 2 is the review of the port itself: findings
that were fixed during review, then the open points.

## Part 1: Original branch (`feat/assimp`, 0406448)

### Robustness

1. `AssimpExport.cs` — scenes were exported without a material.
   Problem: the native FBX, Collada, 3DS and X3D exporters crash the process (SIGSEGV) on a scene
   with no material; STL and PLY fail. Reproduced with Assimp 6.0.
   Fix: `AssimpModelExporter.Export` always adds a default material.
2. `AssimpImport.cs` — `AssimpContext.ImportFile` with post-processing.
   Problem: when validation fails during AssimpNet's post-processing step, Assimp has already
   released the scene and the wrapper releases it again; re-importing an Assimp-written binary PLY
   with `ValidateDataStructure` segfaults this way.
   Fix: the importer drives the native API directly (`ImportSnapshots`) and drops its pointer when
   `aiApplyPostProcessing` returns null.
3. `AssimpImport.cs:376` — file parsing in `Resource<T>`'s generator on the render thread.
   Problem: a 100 MB FBX freezes playback and the editor for seconds.
   Fix: parsing and conversion run on a worker; only the GPU upload stays on the render thread.
4. `AssimpImport.cs:121` — mesh node transforms ignored; vertices of all meshes concatenated raw.
   Problem: multi-object files (most FBX/glTF scenes) collapse their objects onto the origin.
   Fix: node hierarchy flattened, transforms baked (Assimp's column-vector matrices transposed).
5. `AssimpImport.cs:447` — `YUpToZUp` / `ZUpToYUp` swapped Y and Z.
   Problem: a swap is a mirror; the model came out inside-out (winding flipped, normals not).
   Fix: both are 90° rotations; the Flip modes mirror and reverse the winding.
6. `AssimpImport.cs:200` — non-triangle faces left uninitialized `Int3` entries with preset None.
   Problem: zero-index triangles in the index buffer (degenerate geometry, wrong `FaceCount`).
   Fix: triangulation always runs; the index buffer only holds validated triangles.
7. `AssimpImport.cs:192` — `Selection = color.X`.
   Problem: vertex color leaked into the selection channel; `ColorRgb` stayed empty.
   Fix: `ColorRgb` carries the vertex color, `Selection = 1`.
8. `Operators/Lib/io/assimp/` — operators outside `Symbols/`.
   Problem: Release packaging only copies `Symbols/**/*.t3`; the operators never shipped.
   Fix: moved to `Symbols/mesh/generate` and `Symbols/io/file`.
9. `AssimpImport.t3` — defaults `Scale = 0`, `Normalize = false`, `SelectedMeshIndex = 0`.
   Problem: a freshly placed operator showed an invisible, single-mesh model.
   Fix: defaults match the C# initializers (1, true, -1).
10. `AssimpExport.cs:548` — direct SharpDX staging buffers and `AppDomain.BaseDirectory` paths.
    Problem: doesn't compile on the Vulkan backend; writes into the (read-only on Linux/macOS)
    install folder.
    Fix: Graphics.Compat readback as in [WriteObj]; folders resolve into the current project.
11. `Core/Core.csproj` — AssimpNetter referenced from Core.
    Problem: widens the dependency for the Editor, the Player and every package.
    Fix: referenced from Lib only.

### Advertised but missing

The commit message advertised `SkeletonBuffers`, `BoneCount`, `AnimationCount` and `AnimationNames`
outputs that were not implemented. The port implements them through the existing skinning chain
(`Setup`, `SkinWeights`, `SkeletonPoints`).

## Part 2: The port

### Fixed during review

1. `AssimpImport.cs` — any settings change cancelled the running load and started a new one.
   Problem: an animated `Scale` re-parsed the file every frame and never produced a result.
   Fix: at most one load per operator; settings that change meanwhile start the next load when it
   completes. The parsed Assimp scenes are cached per file version and preset, so changing
   Scale, Normalize, axis or flip only re-runs the conversion.
2. `AssimpImport.cs` — the status string was formatted in `Update` every frame while loading.
   Fix: formatted on demand in `GetStatusMessage`.
3. `AssimpImport.cs` — a GPU upload failure left outputs pointing at half-updated buffers and was
   retried every frame.
   Fix: outputs are cleared on failure and the failed state is remembered.
4. `AssimpModelExporter.cs` — meshes on the root node.
   Problem: the 3MF exporter silently drops them (empty file).
   Fix: every mesh gets its own child node.
5. `AssimpModelExporter.cs` — point clouds written to STL/3DS/3MF/Collada produced garbage files.
   Fix: rejected with a message naming the formats that store points.
6. `AssimpExport.cs` — `Ply` mapped to binary PLY, which Assimp 6 can't read back.
   Fix: `Ply` writes ASCII; `PlyBinary` is a separate, documented option.
7. Naming — enums renamed to the plural convention (`AssimpPostProcessPresets`,
   `AssimpAxisConversions`, `ExportFormats`).

### Open points

1. ~~Native crashes can't be caught in-process.~~ Imports **and exports** run in a child process.
   Inside TiXL the child is the editor executable itself (`TiXL --assimp-host ...`, via
   `IsolatedAssimpHost` in Core), so self-contained builds need no `dotnet` installation. Outside an
   editor the `AssimpImportHost` launcher built into Lib's output is used with a `dotnet` executable.
   A native crash becomes an error status, a hang is killed on cancel or after 10 minutes, and the
   child exits as soon as the editor goes away (it watches its stdin pipe). Results come back as
   validated binary files (`AssimpModelDataSerializer`, fuzzed with truncation and bit flips);
   exports are serialized to the child the same way and a failed export removes its partial file.
   Only when no host is available at all do callers run in-process, and status/metadata says so.
2. In-process fallback (isolation switched off, or a non-editor host without `dotnet`) still can't
   abort a native hang; the render thread is never blocked, but the global Assimp lock stays held for
   that session. Imports in this mode are also not protected from a native crash. The setting and
   Metadata make the difference visible.
3. ~~Materials and textures are not imported.~~ Implemented: PBR materials with base color,
   emissive, normal and a CPU-baked roughness/metallic/occlusion map; textures decoded on the worker,
   reused across transform-only reloads and released from CPU memory after upload. Not handled:
   UV channel per texture (always UV0), texture transforms, alpha blending modes.
4. Concave N-gons in `Geometry` are fan-triangulated by [GeometryToMesh] (existing limitation of
   `GeometryMeshCompiler`); `MeshData` uses Assimp's proper triangulation.
5. Four influences per vertex (Assimp `LimitBoneWeights`), matching the skinning shader.
6. Export writes no skins, animations or materials.
7. Very large models (millions of vertices) cause one hitch when the finished load is uploaded to
   the GPU on the render thread; the hard limit is 50 M vertices / triangles.
8. Assimp's own Collada exporter writes key times ×1000 (Assimp bug); files from other tools are
   unaffected.
9. GlTF and OBJ exports write sidecars (`.bin`, `.mtl`) named after the main file. The number
   reservation covers the main file, so a sidecar with that name can be replaced; the operator
   documents this in its help text.

### Hardening pass (2026-11-09)

Brought `origin/multi/feat/assimp` onto this branch and closed the remaining robustness gaps:

- **Exports are isolated too.** `AssimpIsolatedImport.Export` serializes the worker snapshot to a
  temp file, runs `--export` in the same child-process host, and deletes a partial output file on
  errors, crashes and timeouts. `[AssimpExport]` has an `IsolateExport` input (default on) and falls
  back to in-process with the status saying so. This matters because the native FBX/3DS/Collada/3MF
  exporters are the same code that can segfault on unusual scenes.
- **The editor hosts the worker itself.** `IsolatedAssimpHost` in Core recognises
  `TiXL --assimp-host ...` at the very first line of the entry point and runs the operation instead of
  booting the UI. `AssimpIsolatedImport` prefers that form whenever it runs inside TiXL and only uses
  the standalone `AssimpImportHost` + `dotnet` elsewhere. Self-contained release builds therefore
  keep process isolation without shipping a runtime; the packaging gap from the branch is closed.
- **The isolation switches reload.** `IsolateImport` is not part of the conversion settings, so
  toggling it used to affect only the next load. It now cancels a running load and reloads
  immediately; both operators report the mode they actually used ("Imported in-process",
  "Exported ... (in-process)") instead of staying silent when isolation was switched off.
- **`TriggerReload` re-reads materials.** In isolated mode the material identity is the model's
  `FileVersion`, which does not change when only a texture next to the model is edited. Reloads now
  never reuse materials, so the documented "reload after changing referenced files" behaviour also
  applies to textures.
- **Input size limit.** `AssimpModelLoader` rejects files over 2 GB before handing them to Assimp,
  which expands its input in memory several times. The 50 M vertex/triangle limit is unchanged but
  now sits behind a cheap, local check instead of an OOM.
- **Texture memory budget.** One import may retain at most 1 GiB of decoded pixels across all
  materials; further textures (and an oversized baked RMO map) are skipped with a warning instead of
  exhausting memory. Image dimensions are read from the header before decoding, so a small file that
  declares huge dimensions can't allocate the image first. Colors, UVs and opacity read from
  materials are sanitized against NaN/infinite values.
- **CPU buffers are released after upload.** `AssimpModelData.DropUploadedBuffers` clears the vertex,
  triangle, point and skin arrays once the GPU buffers and the `Setup` exist. Without it the CPU copy
  stayed alive for the lifetime of the model, roughly doubling its memory footprint. Element counts
  are carried separately and restored by the serializer, so outputs and Metadata stay correct.
- **NaN/infinity sanitizing in conversion.** Vertex positions, tangents, UVs, vertex colors,
  joint rotations, animation key times and values are checked. Invalid values are replaced (zero,
  white, identity) and counted in the warnings instead of propagating into bounds, skinned matrices
  or GPU buffers.
- **Serialization limits.** `AssimpModelDataSerializer` now bounds arrays by **byte** count rather
  than `int.MaxValue / elementSize`, which previously rejected valid >26 M-vertex isolated imports.
  A corrupt result is rejected before allocating (8 GiB total, 4 GiB per array).
- **Thread-safety.** `AssimpNative.Version` is read on the render thread while a worker loads the
  library, so it is volatile. `_pendingLoad` is volatile because the status provider may read it
  off the render thread. The import outcome carries the isolation setting of its request instead of
  re-reading the input when the result arrives.
- **Failure cleanup.** `AssimpModelExporter` deletes its output when anything throws; the export host
  and the isolated export path delete the reserved file on crash/timeout/cancel. The host launcher
  reports assembly-load failures as text with exit code 65 instead of an unhandled exception.

Verification: Release build of Lib with analyzers is clean for the new files; headless host runs
cover probe, isolated import (OBJ → binary result, plain and textured, with sane vertex/triangle
counts), isolated export (OBJ and GLB, quads preserved), re-import of the exported GLB, corrupt-result
rejection and the 2 GB input limit. The editor integration test
`Tests/Editor.IntegrationTests/AssimpImportExportTests` passes on Windows against the Release editor:
isolated and in-process imports agree (280 vertices / 98 triangles / 3 meshes for the Examples glTF),
both exports write numbered files, and no errors are logged; GraphAcceptance/ProtocolBasics stay green
(8/8). The same test passes on **arton-linux** (LMDE 7, .NET 10.0.403): the full Release solution
builds with 0 errors and the editor test finishes in 6 s, so the editor-hosted worker, the Linux
native library and the export readback all work there. The Linux run also caught an invalid `--`
inside an XML comment in `AssimpImportHost.csproj`; fixed. The test passes with the standalone
`AssimpImportHost` folder removed on Windows, proving the editor executable hosts the worker;
`TiXL.exe --assimp-host ... --probe` and `dotnet TiXL.dll --assimp-host ... --probe` both answer.
See `.tests-manual/assimp-import-export.md` for the manual editor recipe.

### Visual pass (2026-11-09, Windows, Direct3D 11)

Ran the editor checks with generated assets in `D:\TiXLDEV\assimp-test-assets` (a two-bone
skinned/animated glTF and quad-cube OBJ+MTL variants).

**Verified**

- PBR materials: the DamagedHelmet example and a single-material OBJ+MTL render with their base colors.
- N-gon geometry: the 6-quad cube imports as 8 points / 6 faces / 1 part; `[BevelGeometry]` produces
  80 points / 114 faces / closed volume 0.976 and renders a rounded cube.
- Multi-object: `gltf-scene-test-01.glb` reports `MeshCount 3` and draws all three objects.
- Hot reload: overwriting a project asset changed `VertexCount` from 24 to 3 automatically.
- A missing file clears the model and the editor stays alive; a valid path recovers.

**Fixed after the pass** (renderer-side, not in the converter)

- **Multi-material `[DrawScene]`:** every material dispatch drew the first chunk because the draw
  relied on `Draw`'s `startVertex` to offset the shader's `SV_VertexID`; D3D11 and Vulkan did not
  honor it here. `_DispatchSceneDraws` now binds a cached per-dispatch triangle sub-view and draws
  from 0, and the D3D11 backend creates structured-buffer SRVs with the `BUFFER` dimension for
  non-zero element ranges. Verified: stacked red/blue cubes and a six-color cube render on both
  D3D11 and Vulkan.
- **GPU skinning render:** on D3D11 the compute UAV stayed bound after the dispatch, so the
  following draw could not bind the same buffer as a shader resource. The D3D11 backend now unbinds
  the compute UAV slots after every dispatch. Verified: the skinned arm bends at t=0.8 on D3D11;
  Vulkan already rendered it.
- **Broken file at an absolute path:** the earlier run looked like the old model survived, but that
  was the 0.2 s "Loading…" window: polling the status provider shows Notice while loading, then
  Error (`Failed loading broken.obj: Assimp could not read the file: OBJ-file is too small.`),
  `Metadata` is cleared and the output buffers stay empty. Recovering with a valid path restores
  24 vertices. The editor stays responsive throughout; the check used a new `getStatus` request in
  the debug protocol.

**Environment:** compute shaders need `TIXL_BACKEND=d3d11` on this Windows machine; its default
Vulkan path fails to load `spirv-opt`. The Vulkan checks ran on arton-linux.

**Additional robustness checks (2026-11-09)**

- **Export fuzzing.** The isolated export path was driven directly (serializer → 
  `AssimpIsolatedImport.Export`) with 7 deliberately malformed snapshots × 15 formats — obj, fbx,
  collada, 3ds, 3mf, x3d, glb2, gltf2, stl, stlb, ply, plyb, assbin, assxml, assjson — 105 runs.
  75 exports succeeded (valid, 1e30-scaled, 1e-30-scaled, all-zero and fully degenerate index
  buffers); 30 were rejected in the worker with a readable error before the native exporter ran
  (NaN and +Infinity positions: `Vertex 0 has an invalid position …`). Result: no hang, no native
  crash, no orphaned child, and no leftover output file on any error path.
- **macOS cross-build.** `Lib` plus the host launcher build for `osx-arm64`
  (`dotnet build Operators/Lib/Lib.csproj -c Release -r osx-arm64`) with 0 errors; the output
  contains `Lib.dll`, `AssimpImportHost/AssimpImportHost.dll` and `libassimp.dylib`. Not run on
  macOS hardware.

### Verification

- Lib builds on Linux in Debug and Release (analyzers on) without warnings in the new files.
- Bundled natives for win/linux/osx × x64/arm64 land in `runtimes/<rid>/native` of the package
  and are probed explicitly by `AssimpNative`.
- Converter checked headless against glTF sample models: node transforms, multi-mesh chunks,
  welded N-gon geometry, normals/tangents unit length, valid indices, rest-pose skinning error
  < 1e-6, and animated skinned bounds within 1e-4 of an independent SharpGLTF evaluation, also with
  Normalize + YUpToZUp.
- Round-trips through obj, fbx, glb, gltf, stl, ply, collada, 3mf, x3d for mesh, geometry and points.
- 64 concurrent imports/exports with random settings and cancellation: no crash, corrupt files
  fail cleanly.
- Isolated imports produce results bit-identical to in-process ones; ~100-150 ms process overhead.
  A child that dereferences a bad pointer, one killed with SIGKILL, a cancelled import and a killed
  parent were all checked: the parent keeps running and no child is left behind.
- Editor pass on Windows (D3D11) and arton-linux (Vulkan): see the visual pass and the additional
  robustness checks above; the manual recipe is in `.tests-manual/assimp-import-export.md`.

### Deployment-readiness pass (2026-11-19)

Type-compatibility audit of both operators against the current branch (post 11-17 merge), plus a
deploy-state check. No code changes needed; the ops, worker hosting and natives were already in sync.

- **Type compatibility (all verified against current sources):** `MeshData` (MeshBuffers) → mesh
  ops; `Geometry` (MeshGeometry, N-gons preserved) → geometry ops and [VolumeFromMesh];
  `Points` (BufferWithViews of `Point` structs) → the GPU point ops (VisualizePoints, DrawPoints2,
  DrawTubes, …); `Setup` (SceneSetup) → [DrawScene] (`Scene` input), [SampleGltfAnimation],
  [PoseToSkinMatrices] for skinning/animations; `Material` → PbrMaterial with the additive RMO
  contract (reader bakes factors into the RMO map and zeroes the params, matching the shader).
  SDF/volume interop runs over [VolumeFromMesh] (takes MeshGeometry) → volume ops →
  [VdbToSdfField]. The 11-17 merge added `SceneSetup.SceneMaterial.AuthoredRoughnessOffset/Metal`
  for [LoadGltfScene] only; no shader or `PbrParameters` semantic change touches this path.
- **Known interop gap (unchanged, repo-wide):** `VolumeFromPoints` wants CPU `StructuredList`
  points; the import's Points output is the GPU point buffer. Mesh/geometry→volume works;
  points→volume would need a GPU→CPU point converter op that doesn't exist yet.
- **Deploy state:** `Editor/bin/Release` is bit-identical with the repo for both ops' .t3/.t3ui/.cs,
  Lib.dll and AssimpImportHost.dll built 2026-11-19 06:14 (after the last source change), natives
  present for all seven RIDs, `TiXL --assimp-host` probing verified previously. Docs: symbol and
  input descriptions were already complete; the missing wiki pages (never exported) were generated
  in exporter format via `Scripts/generate_assimp_help_pages.py` — 2 op pages + 2 README lines +
  index.json entries (838 ops). Next editor "Export as WIKI" run will simply reproduce them.
- **t3 defaults** re-verified against the C# initializers, all 10 inputs match (including the
  enum-mapped PostProcessFlags=Basic, AxisConversionMode=None).
