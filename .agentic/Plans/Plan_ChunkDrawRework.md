# Chunk draw rework (DrawMeshChunksAtPoints)

Status (2026-11-27): **MDII implemented in `[DrawMeshChunksAtPoints]`** (same inputs; the graph uses
`_DrawChunksIndirect`). `_BuildChunkDrawData`, the MDI/MDII spike ops and the dead
`MeshChunks-Update*` shaders are deleted. Vulkan only: the Windows Player still defaults to D3D11, where
the op renders nothing. The sections below are the research and spike history.

## How it works today

`DrawMeshChunksAtPoints` → `_BuildChunkDrawData` (C#) → `Draw(vertexCount)`.

1. `_BuildChunkDrawData` reads back `MeshBuffers.ChunkDefsBuffer` and the `ChunkIndices` buffer
   (synchronous staging copy + Map).
2. On the CPU it expands one `{PointIndex, FaceIndex}` entry per drawn face and uploads that table.
3. The draw uses the CPU-known `totalFaces * 3`; the VS (`DrawChunksAtPoints.hlsl`) reads
   `DrawData[id/3]` → `FaceIndices[...]` → `PbrVertices[...]`.

The rebuild runs only when `HashCode(pointCount, chunkCount, chunkIndexCount, chunkDefs.NativePointer,
chunkIndices.NativePointer)` changes, or every frame with `UpdateDrawData`.

Before 314811a12 (2026-09-04) this was a GPU chain (chunk sizes → prefix scan → **readback of the face
total** → per-face binary search → draw). `MeshChunks-UpdateChunkSizes.hlsl` and
`MeshChunks-UpdateFaceDrawData.hlsl` are left over from it and no longer referenced by any `.t3`.

## Findings

### Correctness
- **Stale table on content changes.** `SetupStructuredBuffer` keeps the buffer object when the size is
  unchanged and only calls `UpdateSubresource`, so `NativePointer` stays the same. Any change that keeps
  the chunk count and alters face counts or ranges (Voronoi reseed, a different subdivision with the
  same part count, an edited `ChunkIndices` buffer of the same length) leaves the old table in place.
  The draw then picks faces from wrong ranges. GPU-written chunk indices are never detected.
  This is the reason `UpdateDrawData` exists.
- The leftover `MeshChunks-UpdateFaceDrawData.hlsl` writes `{PointIndex, int3}` (16 B). The current VS
  reads `{PointIndex, FaceIndex}` (8 B). Being dead, it does no harm, but it would mislead whoever
  revives it. Delete both leftover shaders.
- `_GetSceneDefinitionPoints` names its chunk-*index* output `ChunkDefsBuffer`.
  - It decomposes node matrices into position, rotation and `abs(scale)`, so mirrored and sheared glTF
    nodes render wrong.
  - Scale is applied only when `UseStretch` is on.
  - The chunk path uses a single `PbrMaterial` for every node, so the scene's materials are lost. That
    is the "flawed glTF integration": `DrawScene` / `_DispatchSceneDraws` keeps materials by issuing one
    draw per node, and the chunk path has no equivalent.

### Performance
- Two synchronous readbacks per structure change (a GPU stall), an O(totalFaces) CPU loop, and a
  `totalFaces * 8 B` upload. On a GPU-driven chunk index this has to run every frame.
- The draw is non-indexed through a per-face indirection, so every face corner runs the VS (no
  post-transform reuse). That is inherent to "one draw, many chunks" and is kept below.

### Indirect draw is available on both backends
- `DeviceContext.DrawInstancedIndirect` → D3D11 `DrawInstancedIndirect` / Vulkan `vkCmdDrawIndirect`,
  with `BarrierIndirectArguments` handling the compute→indirect hazard (`VulkanCommandList.cs:612`).
  `DispatchIndirect` exists too.
- The catch is the args buffer. D3D11 forbids `BufferStructured | DrawIndirectArguments`, Compat drops
  `BufferAllowRawViews` (raw UAVs can't be made), and the Vulkan backend has no texel-buffer views, so
  `RWBuffer<uint>` (the typed-UAV route `LegacyParticleSystem` uses on D3D11) isn't portable.
  **Portable recipe:** a compute shader writes the 4 uints into a 16-byte `RWStructuredBuffer<uint>`,
  then `CopySubresourceRegion` / `CopyResource` copies them into an `IndirectBuffer`
  (`DrawIndirectArguments`, not structured), and that buffer is passed to `DrawInstancedIndirect`.
  Copies are allowed on every buffer (`Translate.ToBufferUsage` always adds CopySource|CopyDestination).
- Suspected but not verified: `DrawPointsDOF` hands a `StructuredBufferWithViews` straight to
  `DrawInstancedIndirect`. That is invalid on D3D11 and lacks `VK_BUFFER_USAGE_INDIRECT_BUFFER_BIT` on
  Vulkan. Worth checking with the validation layer; the same recipe would fix it.

## Change detection: use dirty flags, not buffer identity

GPU-written buffers do carry dirtiness: `ExecuteBufferUpdate` evaluates its compute command inside the
`BufferWithViews` output's update, so a re-dispatched producer makes the consumer's input dirty even
though the buffer object is unchanged. The stale-table bug comes from `_BuildChunkDrawData` overriding
that signal with its `NativePointer` structure key.

Rule (for both the CPU and the GPU version):
- Rebuild when `Mesh.DirtyFlag.IsDirty || ChunkIndices.DirtyFlag.IsDirty`, or when the point count
  (from the buffer size) changed.
- Read the flags before `GetValue`, which clears them.
- Ignore `GPoints` dirtiness: positions change every frame but don't affect the table.

**Step 0 (quick fix, CPU version kept):**
- Swap the pointer key for that rule and retire `UpdateDrawData` (keep the slot, hide it).
- Update becomes automatic and existing graphs are unaffected.
- The readback remains, but runs only on real mesh or chunk-index changes.

The GPU version keeps the same gate, so a static scene runs zero compute passes per frame. An animated
chunk-index source rebuilds every frame: a stall on the CPU version, three tiny dispatches on the GPU
version.

Limit: a buffer written without the `ExecuteBufferUpdate` pattern would not be caught.

## Proposal (ideal tier: same op, same inputs, no readback, always correct)

Replace `_BuildChunkDrawData` with a GPU path that runs every frame. It is cheap enough that no change
detection is needed, which removes the stale-table bug and the manual trigger in one go.

Per frame, with N = point count (known on the CPU from the buffer size):

1. **CS `ChunkFaceCounts`** (N threads): `count[p] = ChunkDefs[chunk(p)].FaceCount`, using the same
   `chunk(p)` rule as today (`ChunkIndices[p % len]` or `p`, wrapped by chunk count).
2. **Inclusive prefix scan** → `FaceEnds[N]`, using the existing `PrefixSum` op (`prefix-scan.hlsl`).
   Check its maximum element count first: the DX SDK two-level scan covers 512×512 = 262 144. Above
   that it needs a third level, or a decoupled look-back scan.
3. **CS `WriteChunkDrawArgs`** (1 thread): `{FaceEnds[N-1] * 3, 1, 0, 0}` into a 16-byte structured
   buffer, then copied into the indirect args buffer (recipe above).
4. **`DrawInstancedIndirect`** replaces `Draw`. The VS finds its point by `upper_bound` binary search
   over `FaceEnds` (log2 N ≤ 20 steps; neighbouring vertices share a path, so the reads are coherent).
   It then computes `faceInChunk = face - (FaceEnds[p] - count[p])` and reads
   `FaceIndices[def.StartFaceIndex + faceInChunk]`.

This needs no per-face table, so no buffer has to be sized from an unknown face total. That was the
real obstacle to dropping the readback: every face-length buffer needs the total on the CPU.

**On the user's idea of a face-length index buffer:** it trades the per-vertex search for a fill pass
plus a buffer of unknown size. A per-face fill pass would have to over-allocate
(N × max chunk faces, e.g. 5 M entries for 100 glTF nodes of 50 k faces) or grow from an async readback
one frame late. Only add it if profiling shows the VS search costing more than about 0.3 ms at
realistic sizes. Shadow and main pass would then share it.

**Rejected alternatives:**
- *Instanced with a bound (`Draw(maxFaces*3, N)`, degenerate the excess faces):* no compute at all, but
  the waste equals the chunk-size variance, so glTF scenes explode.
- *One indexed indirect draw per chunk:* gains vertex reuse, but costs K draw calls (thousands for
  fractures where every point has its own chunk).
- *Multi-draw-indirect:* not in D3D11.

**Compatibility:**
- Inputs and outputs of `DrawMeshChunksAtPoints` stay as they are, and the VS bindings stay the same
  except t2 (`DrawData` → `FaceEnds`).
- `UpdateDrawData` becomes a no-op. Keep the slot so saved graphs load, and hide it in the `.t3ui`.

**Implementation shape:** a C# code op `_ChunkDrawArgs` (inputs GPoints, Mesh, ChunkIndices; outputs
`FaceEnds` and the indirect `Buffer`) that owns the two small compute shaders and calls the scan. This
keeps the `.t3` graph change down to swapping `_BuildChunkDrawData` + `Draw` for
`_ChunkDrawArgs` + `DrawInstancedIndirect`.

## Alternative without search or table: multi-draw indirect (Vulkan)

With one draw, mapping a flat vertex id to (point, face) for variable chunk sizes always needs a search
or a table. MDI removes the flat id range instead: one `vkCmdDrawIndirect(args, drawCount = N,
stride 16)` with one record per point.

- **Args CS** (one thread per point, no scan): `{FaceCount(chunk(p)) * 3, 1, 0, firstInstance = p}`.
  N is the point count from the buffer size, so the CPU knows the draw count.
- **VS:** `p = SV_StartInstanceLocation` → `def = ChunkDefs[chunk(p)]` →
  `face = def.StartFaceIndex + SV_VertexID / 3`.
- **Verified with slangc 2026.18:** `SV_VertexID` → `gl_VertexIndex - gl_BaseVertex` (zero-based per
  sub-draw) and `SV_StartInstanceLocation` → `gl_BaseInstance`. `shaderDrawParameters` is already
  enabled (`VulkanBackend.cs:260`).
- **Missing:**
  - a `DrawIndirect(buffer, offset, drawCount, stride)` overload (`VulkanCommandList.cs:582` hardcodes
    1) in Graphics/Compat;
  - enabling the core `multiDrawIndirect` feature;
  - clamping to `maxDrawIndirectCount`.
- **Risks to measure:**
  - fixed front-end cost per sub-draw at 100k+ points;
  - half-empty waves for tiny chunks (vertices of different sub-draws don't share a wave). The
    prefix-sum/search path packs those perfectly.
- **D3D11** has no MDI and SM5 lacks `SV_StartInstanceLocation`, so Windows keeps the
  prefix-sum/search path. Only the args shader and the VS prologue differ between the paths.
- **Not first choice:**
  - mesh/task shaders (Vulkan only; the proper amplification model, but a new pipeline type);
  - power-of-two size-class buckets with padded instanced draws (about 16 draws, less than 2× waste,
    compaction list sized by point count).

**Platform support:**
- Needs both `multiDrawIndirect` and `drawIndirectFirstInstance` (without the latter, `firstInstance`
  must be 0). Neither is enabled in `VulkanBackend.cs` yet.
- Desktop Linux drivers (RADV, ANV, NVIDIA, NVK) are expected to expose both. Confirm with `vulkaninfo`.
- MoltenVK: unverified whether it reports them. Metal has no native MDI, so emulation (one Metal draw
  per record) would bring back per-draw CPU cost.
- **Decision:** choose at runtime. Use MDI when both features are present and the driver is not
  MoltenVK; otherwise use the prefix-sum/search path, which D3D11 needs anyway.

Next: a Vulkan spike that benchmarks MDI against the search path (100k-face fracture, glTF scene).

## MDI spike results (2026-11-27, Linux/Vulkan, Radeon 8060S, Debug)

**Added:**
- `ICommandList.DrawIndirect(arguments, offset, drawCount, stride)`:
  - Vulkan enables `multiDrawIndirect` + `drawIndirectFirstInstance` when present and splits at
    `maxDrawIndirectCount`;
  - without MDI, Vulkan loops per record; D3D11 always loops.
- `DeviceContext.DrawInstancedIndirect(buffer, offset, drawCount, stride)`.
- Code op `_DrawChunksMultiIndirect`:
  - a compute pass writes `{faces*3, 1, startFace*3, point}` per point;
  - the records are copied into a `DrawIndirectArguments` buffer, then drawn with one command;
  - rebuilt when `Mesh` / `ChunkIndices` are dirty or a count changes.
- Spike op `[DrawMeshChunksAtPointsMdi]`: a copy of `[DrawMeshChunksAtPoints]` using it and
  `DrawChunksAtPoints-mdi.hlsl` (VS: `face = (SV_StartVertexLocation + SV_VertexID) / 3`,
  `point = SV_StartInstanceLocation`).

**Correctness:**
- Pixel-identical to the old op for the static and exploded/rotated fracture and after a cell-count
  change.
- After a same-count reseed the old op shows torn slivers (stale table) and MDI is correct.
- No validation errors.

**Frame time** (median ms, 70 Voronoi chunks cycled over `[GridPoints]`):

| points | steady, old | steady, MDI | rebuilt every frame, old | rebuilt every frame, MDI |
|---|---|---|---|---|
| 10k  | 1.91 | 1.88 | 3.85 | 1.37 |
| 100k | 3.05 | 3.46 | 24.8 | 3.65 |
| 400k | 7.59 | 8.79 | 92.9 | 9.24 |

**Reading:**
- Live chunk indices become cheap: 7–10× faster than the readback path.
- Static draws pay about 13–16 % GPU time for sub-draw overhead and half-filled waves at 100k+ tiny
  chunks. CPU time is equal.
- Follow-up worth trying: `vkCmdDrawIndexedIndirect` over the mesh index buffer, so sub-draws get
  post-transform vertex reuse (the per-face path can't have it). This needs the index buffer created
  with index-buffer usage.

## MDII and chunk-size sweep (2026-11-27, same machine, Debug)

**Added MDII:**
- `ICommandList.DrawIndexedIndirect(args, offset, drawCount, stride)` and
  `DeviceContext.DrawIndexedInstancedIndirect`.
- `MeshChunks-WriteIndexedDrawArgs.hlsl`: 20-byte records `{indexCount, 1, firstIndex, 0, point}`.
- `DrawChunksAtPoints-mdii.hlsl`: `PbrVertices[SV_VertexID]`; slang gives the raw index for a zero
  vertex offset.
- An `Indexed` switch on `_DrawChunksMultiIndirect`, and the `[DrawMeshChunksAtPointsMdii]` spike op.
- **Spike shortcut to replace:** `VulkanConvert` gives every storage buffer index-buffer usage. The real
  version sets it only on mesh index buffers.

**Setup:** about 5M triangles per case, `[GridPoints]` placing chunks, VSync off. Each value is the
median frame ms of 2×100 frames. The editor UI alone measured about 1.2 ms.

| chunk (tris) | points | static old | static MDI | static MDII | rebuild old | rebuild MDI | rebuild MDII |
|---|---|---|---|---|---|---|---|
| Voronoi, ~30, uneven | 97k | 3.05 | 3.49 | 3.05 | 25.5 | 3.67 | 3.06 |
| cube 48 | 97k | 4.08 | 4.16 | 3.40 | 34.1 | 4.15 | 3.42 |
| cube 588 | 10.6k | 4.10 | 4.09 | 3.22 | 43.4 | 4.14 | 3.31 |
| cube 5808 | 1k | 4.09 | 4.09 | 3.76 | 41.5 | 4.12 | 3.52 |

All three paths render pixel-identical images in every case. No validation errors.

**Reading:**
- The per-draw overhead (MDI vs old) shows only for tiny uneven chunks; from about 50 triangles it is
  gone.
- MDII is never slower than the old per-face table. It saves 20–30 % of the draw cost (after
  subtracting the UI floor) where vertices are shared, and ties on flat-shaded Voronoi cells (little
  reuse).
- Rebuilding every frame costs MDI/MDII ≤ 0.2 ms. The CPU table costs 25–43 ms.
- **Recommendation:** MDII as the single path once DX11 is retired. Keep the 16-byte MDI only if a mesh
  without an index buffer must be supported.
- **MoltenVK** (no native MDI; emulated per record) remains the open case.

## GPU culling and LOD on top of MDI (idea, 2026-11-27)

The args pass already decides per point what to draw, so it is the natural place for culling and LOD:
- frustum test: `vertexCount = 0`;
- LOD: pick among consecutive per-mesh LOD chunks by distance;
- later Hi-Z occlusion.

**Prerequisites:**
- **A bounds pass:** per-chunk AABB or sphere, built once per mesh change. Either the producers
  (`GeometryMeshCompiler`, glTF) fill it on the CPU, or a compute pass over each chunk's vertex range
  does it for GPU meshes. It is stored in the per-chunk attribute buffer.
- **Records per camera:** shadow and cube passes need their own records.
- **Coordinate with the contributor working on LOD and culling for large scenes.**

## Integration fixes (independent of the draw rework)

- `_GetSceneDefinitionPoints`:
  - Rename the output to `ChunkIndices`.
  - Keep the decomposed scale signed and document that shear is lost. Alternatively store the full
    matrix: points can't carry one, which argues for the per-chunk/per-instance data below.
- Delete the two leftover shaders.

## Per-chunk materials and attributes → `[CustomDrawChunks]` (user idea, 2026-11-27)

Keep `MeshChunkDef` frozen at 16 B. Existing shaders and saved buffers depend on that layout. Add a
**parallel, optional** `ChunkAttributesBuffer` on `MeshBuffers`, indexed by chunk index (a null buffer
means defaults, so every producer stays valid). Suggested first layout:

| field | source |
| --- | --- |
| `MaterialIndex` (int) | glTF primitive material; geometry part material attribute |
| `Pivot` (float3) | `GeometryMeshCompiler` part pivot (already computed) |
| `SeedIndex`, `F1`, `F2` | geometry part attributes (today copied into the points by `GeometryToMeshChunks`) |
| `BaseColor` (float4) | part color / glTF base color factor |

**Materials:**
- Scalar PBR parameters (base color, roughness, metal, emissive factor) go into a `MaterialParams[]`
  structured buffer indexed by `MaterialIndex`. That is easy and works in a single draw.
- Textures can't be indexed per chunk without bindless (not available on the D3D11 path). Options:
  - (a) one indirect draw per material, where step 1 counts per material and the scan is segmented by
    material;
  - (b) a `Texture2DArray` of resized maps.
- (a) mirrors `_DispatchSceneDraws` and keeps quality, so it is recommended. It turns "glTF via chunks"
  into a real replacement for per-node draws.

**`[CustomDrawChunks]`:** modelled on `[CustomDrawMesh]`. A template shader runs the chunk VS above and
exposes a user hook that receives `Point`, `chunkIndex`, `faceInChunk`, the `ChunkAttributes` entry and
the vertex, and returns the modified position, color and attributes. This only becomes cheap once the
per-frame GPU path above exists: with the CPU table, every attribute-driven variation would need
another readback.

## Order

0. Dirty-flag rebuild gate in `_BuildChunkDrawData` (see "Change detection").
1. GPU draw args + VS search in `DrawMeshChunksAtPoints` (ideal tier).
   - Verify against `.tests-manual/geometry-ops.md`, the D3D11 and Vulkan validation layers, and the
     benchmark suite.
2. Integration fixes (`_GetSceneDefinitionPoints`, dead shaders).
3. `ChunkAttributesBuffer` + material params buffer (glTF and `GeometryToMeshChunks` fill it).
4. `[CustomDrawChunks]`.
5. Per-material segmented draws for textured glTF scenes.

## Open checks before step 1

- `PrefixSum` op: maximum element count, and whether it is inclusive per its `IsInclusive` flag on both
  backends.
- Whether `CopySubresourceRegion` between a structured and an indirect-args buffer passes the D3D11
  debug layer. Expected to, since it is a common pattern, but measure it.
- Ideally run step 1 as a spike (a new op beside the old one) on a 100 k-face fracture and a glTF scene
  before replacing anything.
