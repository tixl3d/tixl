# Plan: Scene Document (format-neutral scene loading)

Status: **Design agreed with maintainer 2026-09-03; not started**
Last update: 2026-09-03
Related: `Plan_ProceduralGeometry.md` (parts, chunks, `PlaceGeometryAtPoints`),
`Plan_GltfAnimation.md` (skeletons, poses, skinning ops)

## Problem

`SceneSetup` is doing four jobs at once:

1. It is the **result of parsing a glTF file**: node tree, meshes, materials,
   skeletons, animation clips.
2. It is the **draw setup**: GPU `MeshBuffers` per node, `PbrMaterial` GPU objects,
   skin-weight buffers, and the flattened `Dispatches` list that `DrawScene` walks.
3. It is a **persisted parameter**: `IEditableInputType` whose `NodeSettings`
   (visibility, material override per node) are saved into projects, written by
   `LoadGltfScene` into its own `Setup` input.
4. It is the **skeleton/animation carrier** for every skinning op
   (`SampleGltfAnimation`, `PoseToSkinMatrices`, `BindToSkeleton`, `RetargetPose`,
   ... all take `InputSlot<SceneSetup>` + `SkeletonIndex`).

Consequences: the mesh data exists only as GPU buffers, so no CPU geometry op can
touch a loaded model without a readback; there is no way to get "the wheels" out
of a car file; `LoadGltfScene` grew side outputs (`Mesh`, `Material`,
`SkinWeights`, `SkeletonPoints` with a `MeshChildIndex` selector) to work around
that; and `SharpGLTF.Animations.ICurveSampler` leaks into Core through the clips.
Every future format (FBX, USD, OBJ groups) would have to reproduce all four jobs.

## Concept model

Three layers, three types, one direction of flow:

| Layer | Type | Holds | Produced by | Consumed by |
|---|---|---|---|---|
| Description | `SceneDocument` (new) | everything a scene file describes: nodes, meshes as `MeshGeometry`, materials as data, skins, clips, cameras, lights | loaders (`LoadGltfScene`, later FBX/USD) | picker/getter ops, `SceneToDrawSetup` |
| Shape | `MeshGeometry` (exists) | topology + attributes; parts = primitives | `PickGeometryFromScene`, generators, modifiers | geometry ops, `GeometryToMeshBuffers`, `GeometryToChunks`, `GeometryToScene` |
| Draw | `SceneSetup` (exists; conceptually `SceneDrawSetup`) | dispatches, GPU buffers, `PbrMaterial` objects, applied node settings | `SceneToDrawSetup`, `GeometryToScene`, `LoadGltfScene` (derived, for compatibility) | `DrawScene`, `_GetSceneDefinitionPoints` |

Rules:

- A document is **immutable after load** and carries a `Version`. Ops hand out
  references into it (meshes, skeletons); nothing downstream mutates them.
- **Geometry never carries hierarchy or materials.** A part keeps two ints to
  find its way back: `Material` (index into the document's material list) and
  `Node` (index into the node list) as part attributes. `SourcePoint` from
  `PlaceGeometryAtPoints` is the same idea.
- **The draw setup owns GPU objects and nothing else.** It is rebuilt from a
  document (or from geometry) and disposed with it; it is never the thing a
  loader hands out as "the scene" in new graphs.
- **Node settings are a parameter of the op that builds the draw setup**, not
  part of the document. They are edited in the UI, never connected, so they do
  not need a connection type at all — an `IEditableInputType` blob
  (`SceneNodeSettings`) with an input UI, but no output UI, no slot color, and
  no bypass/type registration.
- **No hidden copies.** Picking geometry without baking returns the document's
  `MeshGeometry` by reference. Building a draw setup uploads each distinct mesh
  once and shares the buffers between nodes that reference the same mesh
  (glTF instancing), with a cache keyed on `(MeshGeometry, Version)`.

## `SceneDocument` (Core/DataTypes)

Flat arrays, indices instead of object references, so any format maps onto it
and nothing needs a tree walk to find something.

```
SceneDocument
  Name, SourcePath, Version
  Nodes[]        : Name, ParentIndex (-1 root), LocalTransform (Transform), MeshIndex (-1),
                   SkinIndex (-1), CameraIndex (-1), LightIndex (-1), Extras (string JSON, optional)
  Meshes[]       : MeshGeometry — parts = primitives, part attributes Material (int);
                   corner Normal/TexCoord/TexCoord2/Color, point JointIndices (Int4)
                   + JointWeights (Vector4) when skinned
  Materials[]    : SceneMaterialDef — Name, PbrParameters, texture references by
                   *asset address* (string), alpha mode, double-sided. Data only, no GPU.
  Skins[]        : JointNodeIndices[], InverseBindMatrices[], SkeletonNodeIndex
  Skeletons[]    : as today's SceneSkeleton (joint names, parent indices, rest
                   transforms, inverse bind) — derived from Skins at load so the pose
                   ops keep their current model
  AnimationClips[]: Name, Duration, Channels (SkeletonIndex, JointIndex, samplers)
  Cameras[], Lights[]: data only (projection params; light type/color/intensity)
```

Transforms stay local; a `WorldTransforms` accessor computes and caches the
combined matrices once per document. `Transform` moves from `SceneSetup` to
`Core/Rendering` as the FIXME already asks.

Animation samplers: today `ICurveSampler<T>` from SharpGLTF. Keep the interface
for the first iteration but define our own `ICurveSampler<T>` in Core and adapt
SharpGLTF's at load time, so Core stops referencing the glTF library and an FBX
loader can supply its own samplers. Sampling cost is unchanged.

`MeshGeometry` needs no change for this. `Int4` joint indices are a new attribute
value type (`GeometryAttribute<Int4>` — `Int4` exists in Core).

## Operators

### `LoadGltfScene` (existing, guid kept)

- New output `Scene : SceneDocument`. This is the primary output for new graphs.
- `ResultSetup : SceneSetup` stays and is **derived** from the document by the
  same code `SceneToDrawSetup` uses, applying the op's persisted `Setup` node
  settings. Existing projects keep rendering unchanged.
- `Mesh`, `Material`, `SkinWeights`, `SkeletonPoints`, `MeshChildIndex`,
  `CombineBuffer`, `OffsetRoughness/Metallic` stay for compatibility, marked as
  superseded in the description. `CombineBuffer` becomes irrelevant once
  `GeometryToChunks` exists on the document side; keep it honoring the flag.
- File watching stays on `Resource<SceneDocument>` (today `Resource<SceneSetup>`).
- Later, name-only rename to `LoadGltf` (guid unchanged, symbol rename = no
  migration).

### `SceneToDrawSetup` (new)

- In: `Scene : SceneDocument`, `NodeSettings : SceneNodeSettings` (editable blob,
  the moved `NodeSettings` list), `MaterialOverride` (optional `PbrMaterial`),
  `RoughnessOffset`, `MetallicOffset` (replacing the loader's).
- Out: `Setup : SceneSetup`.
- Builds `MeshBuffers` per distinct mesh (`GeometryMeshCompiler` from Lib/Utils —
  move it to Core if the op lives in Core-adjacent code, or keep the op in Lib),
  `PbrMaterial` per material (texture addresses resolved through the resource
  system), skin-weight side buffers from the point attributes, dispatches with
  world transforms. Caches on document `Version` + settings hash; disposes what
  it created.

### `PickGeometryFromScene` (new)

- In: `Scene`, `Mode` (All / ByNodeName / ByNodeIndex / ByMaterial / ByMesh),
  `Name` (string, wildcard `*` allowed), `Index`, `BakeTransforms` (default off),
  `IncludeChildren` (default on for node modes).
- Out: `Geometry : MeshGeometry`, `Count`.
- Without baking and a single mesh selected: returns the document's mesh by
  reference, zero copy. Otherwise builds one geometry: baked world transforms,
  parts appended with `Node` and `Material` attributes, normals rotated
  (same code as `PlaceGeometryAtPoints`). Custom dropdown lists node/material
  names, like `ColorFacesFromAttribute` lists attributes.

### `GeometryToScene` (new, the inverse)

- In: `Geometry`, `Scene` (optional, for the material list), `Material`
  (fallback `PbrMaterial`).
- Out: `Setup : SceneSetup` — one node per part (transform from pivot, mesh
  chunk = part), material from the part's `Material` attribute if the source
  document is connected, else the fallback. This is how a filtered / fractured /
  merged car keeps its paint.

### `GetSceneSkeleton`, `GetSceneAnimation`, `GetSceneCamera` (new, thin)

Index or name → the document element, for graphs that want to route them
explicitly. Not required for the skinning ops (see below).

### Skinning ops (existing)

They keep `Setup : SceneSetup` + `SkeletonIndex` working: the derived setup still
carries `Skeletons` and `AnimationClips` (references into the document, no copy).
Each gets an additional optional `Scene : SceneDocument` input that takes
precedence when connected. When all shipped example graphs are switched, the
`SceneSetup` copies of skeletons and clips can be dropped in a later cleanup.

`SkinMesh` / `BindToSkeleton` work on `MeshBuffers` + weight buffers today.
`GeometryToMeshBuffers` gains emission of the skin-weight side buffer from the
`JointIndices`/`JointWeights` point attributes (second output), so a picked,
beveled, skinned mesh still skins. That closes the "procedural meshes stay
skinnable" contract from the geometry plan.

### Geometry helpers this unlocks (from the geometry plan)

`FilterGeoPartsByAttribute` (keep parts where `Material == n` / `Node in range`),
`MergeGeometry` (multi-input concatenation, attribute union). Both are the
existing part-subset code with a different predicate / direction.

## Phases

### Phase A — Types and loader (no behavior change)

1. `SceneDocument`, `SceneMaterialDef`, `SceneNodeSettings` in Core; `Transform`
   to `Core/Rendering`; Core-owned `ICurveSampler<T>`.
2. `LoadGltfScene` builds a document first, then derives `ResultSetup` from it
   through the new shared builder. `Scene` output added.
3. Accept: every shipped example using `LoadGltfScene` / `DrawScene` / skinning
   renders pixel-identical (visual reference suite); `Fox.gltf` animation plays.

### Phase B — Picking and rebuilding

4. `PickGeometryFromScene`, `GeometryToScene`, `SceneToDrawSetup`.
5. `GeometryToMeshBuffers` skin-weight output; skinning ops' optional `Scene` input.
6. Accept: car-style file → pick wheels by name → fracture → merge with body →
   `GeometryToScene` → `DrawScene` shows original materials on the untouched
   parts; a picked skinned mesh through `SkinMesh` still deforms.

### Phase C — Cleanup

7. Move node settings out of `SceneSetup` into `SceneNodeSettings` used by
   `SceneToDrawSetup`; `LoadGltfScene.Setup` input keeps reading the old blob
   shape (back-compat reader, permanent).
8. Drop skeletons/clips from `SceneSetup` once no shipped graph needs them;
   rename `SceneSetup` → `SceneDrawSetup` only if the persisted type name can be
   read under both names (the `Write` method emits `nameof(SceneSetup)` as key —
   keep accepting it).
9. `LoadGltfScene` → `LoadGltf` name.

## Interface-stability audit (per AGENT_INSTRUCTIONS)

Use cases in the next months and what they need from the document:

- **FBX / USD / OBJ-with-groups loaders** → same document; formats only differ in
  what they fill. Extras as JSON strings absorb format-specific bits.
- **Instancing (same mesh in many nodes)** → `MeshIndex` per node; draw setup
  shares buffers. Already covered.
- **Morph targets / blend shapes** → per-mesh list of delta position/normal
  attributes; optional field on the mesh entry, additive.
- **Cameras / lights from files** → `Cameras[]`, `Lights[]` present from the
  start even if the first loader fills only cameras.
- **Multiple scenes in one glTF** → `SceneIndex` parameter on the loader; the
  document represents one scene. Additive.
- **LODs** → node extras or a `LodGroupIndex` field later; additive.
- **Textures** → by asset address, resolved when building the draw setup, so a
  document never holds GPU textures and can be built off the main thread.

What would force a migration: only the persisted `SceneNodeSettings` shape, and
the `Setup` input of `LoadGltfScene`. Both keep back-compat readers. Everything
else flows through connections and is not serialized.

## Open questions

- Should `SceneDocument` be a connection type with its own wire color (like
  `MeshGeometry`) — yes, it's connected between ops. `SceneNodeSettings` is not.
- Loading off the main thread: the document has no GPU objects, so `Resource<T>`
  could parse asynchronously later; `AsyncComputation<T>` from the geometry work
  fits. Not in scope now.
- Where `GeometryMeshCompiler` lives once Core needs it (`SceneToDrawSetup` as a
  Lib op keeps it in Lib; a Core-side draw-setup builder would need it in
  `Core/Rendering`).
