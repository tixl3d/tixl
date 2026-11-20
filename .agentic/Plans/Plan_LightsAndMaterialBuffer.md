# Plan: Light Types and Material Buffer (deferred ingredients, forward shading)

Status: **not started**, written 2026-11-20 on `main` (32bcb8124). Nothing built or run; findings come from reading.

## Why

Users ask for "deferred rendering". What they actually want is **better lighting and material control**: more light
types than point lights, and post effects that know the surface material. A full switch to deferred would break what
TiXL relies on — custom pixel shaders (`[CustomDrawMesh]`, raymarch templates, field code applied after lighting in
`mesh-Draw.hlsl`), non-PBR looks (unlit, cel, hatched), additive/premultiplied blending and MSAA.

So: keep forward shading as the default, add the missing light types, and write a **material buffer** next to the
existing depth / normal / velocity targets. Together with those, the material buffer holds everything a deferred
lighting pass needs, so deferred becomes an *optional additive op* later instead of a pipeline rewrite.

Non-goals: replacing forward shading, clustered/tiled light culling (only worth it at hundreds of lights; revisit if
point-buffer lights are added), shadow receiving in more draw ops.

## Current State (2026-11-20)

- **Lights**: point lights only. `Core/Rendering/PointLightStack.cs` (`PointLight` struct, 48 bytes, `MaxPointLights =
  8`), HLSL mirror `Operators/Lib/Assets/shaders/shared/point-light.hlsl`, bound as `cbuffer PointLights : register(b3)`.
  Pushed/popped by `[PointLight]` / `[SetPointLight]` (`render/shading/`) on `context.PointLights`, so light sets are
  scoped per subtree.
- **PBR**: `shared/pbr-render.hlsl` `ComputePbr()` loops `ActiveLightCount`, falloff
  `intensity / (pow(dist / range, decay) + 1)`, GGX + Schlick, IBL, emissive, fog. Material inputs live in the static
  `FragmentMaterial frag` (`shared/pbr.hlsl:74`: Roughness, Metalness, Occlusion, albedo, N, …). Included by 6 draw
  shaders (mesh-Draw, mesh-CustomDraw-template, mesh-DrawAtPoints, DrawPointsShaded, DrawLinesShaded,
  RaymarchSDFFieldWithMatTemplate). 8 further draw shaders read the light cbuffer with their own lighting (hatched,
  cel, tubes, ribbons, chunks, point sprites, DrawWithShadows, DrawAtPoints2).
- **Materials**: `[SetMaterial]` → `Core/Rendering/Material/PbrMaterial.cs` (BaseColor, Emissive, Roughness, Specular,
  Metal + maps), pushed on `context.PbrMaterial`.
- **Shadows**: `[SetShadow]` renders one depth map from LightPosition toward LightTarget (`RenderPass.IsShadowPass`);
  only `[DrawMeshWithShadow]` receives it.
- **Render targets**: `[RenderTarget]` (`image/generate/basic/RenderTarget.cs`) has color, depth (R32), normal
  (SV_Target1, world normal) and velocity (SV_Target2, RGBA16F) with `WithNormalBuffer` / `WithVelocityBuffer`;
  velocity sets `RenderPass.VelocityRequested`. Vulkan packs bound targets, so velocity forces a normal target in slot 1
  (see `Plan_ImplicitRenderDefaults.md`). `[RenderScene]` passes depth/normal/velocity/camera as a render bundle.
- **Blending**: draw ops get their blend state from `[PickBlendMode]` (`render/_dx11/fxsetup/PickBlendMode.t3`), a
  graph of 10 `[BlendState]` type ops with one `[RenderTargetBlendDescription]` each and `IndependentBlendEnable` off.
  51 symbols use it. With the flag off, target 0's blend applies to every target: additive draws add into normal and
  velocity today. Both backends already honour per-target state (`Graphics.Compat/Resources.cs:605`,
  `Graphics.D3D11/Convert.cs:83`, `Graphics.Vulkan/VulkanPipelineFactory.cs:160`).
- **SDF materials**: the raymarch template calls `GetField(float4(p, 0))` for distance and `GetField(float4(p, 1))` for
  color (`RaymarchSDFFieldWithMatTemplate.hlsl:477`). `[SetSDFMaterial]` branches on `p.w` in (0.5, 1.5) and sets
  albedo only. Roughness / Specular / Metal are global template parameters plus an RSMO map (lines 62–64, 480–494).
  Other ops reading `p.w`: `ExecuteHeightmapSdf`, `ExecuteImage2dSdf`, `ExecuteRepeatFieldAtPoints`.
- No shader writes SV_Target3 or higher.

## Design

### 1. Per-target blending (prerequisite)

Replace the `[PickBlendMode]` graph with a C# op that returns cached `BlendState`s (one per mode, created once, no
per-frame allocation), keeping the symbol id and the `BlendMode` input so the 51 users don't change.
`IndependentBlendEnable = true`, and per mode:

| Blend mode | Target 0 (color) | Targets 1–4 (normal, velocity, base color, material) |
|---|---|---|
| None / opaque | as today | write, no blend |
| Normal (alpha) | as today | write, no blend (alpha cut-off decides coverage; see open question 1) |
| Additive, Multiply, Invert, BlendOnWhite | as today | write mask 0 |
| PreMultiplied | as today | write mask 0 |

This alone fixes additive particles corrupting normal and velocity (which `Plan_ImplicitRenderDefaults.md` works around
with `a = 1`). Custom `[BlendState]` graphs stay untouched.

### 2. Light types

Generalize `PointLight` into `Light` (C# in `Core/Rendering/`, HLSL in a new `shared/lights.hlsl`;
`point-light.hlsl` keeps a compatible include). Keep the first fields (position, intensity, color, range, decay) at
their current offsets so the 8 non-PBR shaders keep compiling and treat every light as a point light until ported.
Append:

- `uint Type` (0 point, 1 spot, 2 directional, 3 sphere area, 4 rect area)
- `float3 Direction`, `float SpotCosInner`, `float SpotCosOuter`
- `float2 Size` (area extent), `float3 Right` (rect orientation), padding to 16 bytes

Keep the cbuffer at b3 and the stack semantics; raise the cap only if the cbuffer stays under 64 KB (structured buffer
is a later step, together with point-buffer lights).

Shader side: one function `EvaluateLight(Light l, float3 worldPos, out float3 L, out float3 radiance)` used by
`ComputePbr()`. Spot = point × smoothstep cone; directional = constant L, no falloff; sphere/tube area = representative
point (Karis 2013) with roughness widening; rect area = LTC (two 64×64 lookup textures, bound like `BRDFLookup`).

Ops: `[SpotLight]` / `[DirectionalLight]` / `[AreaLight]` (and `Set…` variants if the point-light pairs are kept as
the pattern), all pushing on the same stack. Gizmos in the 3D view for direction and cone.

Shadows: `[SetShadow]` already renders a perspective map from position toward target, i.e. a spot camera. Give it an
optional light reference (store the light op's `Guid`, not the value) so cone and shadow stay matched; directional
needs an orthographic projection. More receivers / multiple shadow maps are out of scope.

### 3. Material buffer

`[RenderTarget]` gets `WithMaterialBuffer` (opt-in, like velocity) and two outputs:

- **SV_Target3 BaseColor**: RGBA8, rgb = unlit albedo × material color, a = occlusion.
- **SV_Target4 Material**: RGBA8, r = roughness, g = metalness, b = specular, a = shading model id / 255
  (0 = not written — cleared value, 1 = PBR, 2 = unlit / custom, skip relight).

Emissive is not stored: deferred lighting adds to the forward color, which already contains emissive.

Rules, following the velocity precedent:
- With material on, normal and velocity slots are always bound (Vulkan packs targets; a gap would shift slots).
- `RenderPassState.MaterialRequested` lets draws skip the work when no target listens.
- MSAA: resolve like normal (taking sample 0 for the id, not an average — averaged ids are meaningless).
- The render bundle gets `BaseColor` and `Material` textures; `[HandleTextureBundle]` passes them through.

Writers: a helper in `pbr-render.hlsl`, e.g. `WriteMaterialTargets(out float4 baseColor, out float4 material)`
reading `frag`, called by the 6 PBR shaders. Non-PBR draw shaders write id 2 (or nothing → 0, also skipped).

### 4. SDF material pass (`p.w = 2`)

Third field evaluation at the hit point only (not per step), so the cost is one extra `GetField` per pixel.
- `[SetSDFMaterial]` gets Roughness / Metal / Specular inputs; for `p.w` in (1.5, 2.5) it writes them to `f.rgb`.
- The template calls `GetField(float4(p, 2))`, and where no material was set (sentinel, e.g. `f.a < 0`) falls back
  to its global parameters + RSMO map. With `[SetSDFMaterial]` present, its values replace them.
- SDF blending ops must mix `f` for `w = 2` exactly as for `w = 1`; check the combine ops and the three ops listed in
  Current State that branch on `p.w`.
- Works in forward immediately (per-object materials in raymarching); also feeds the material buffer.

### 5. Consumers

1. `[ScreenSpaceReflections]` reads roughness (blur/cone width, fade) and metalness (tint by base color) when the
   bundle has a material texture; unchanged without it.
2. Optional `[DeferredLights]` post op on the render bundle: evaluates its own light subtree from depth + normal +
   base color + material and **adds** the result to the color target. Lighting is linear, so forward lights and
   deferred lights sum. Skips id 0 and 2. Transparent surfaces stay forward-lit only. This is where many / expensive
   lights (rect area) get cheap.
3. Later, same inputs: decals, screen-space GI.

## Milestones

| | Scope | Verify |
|---|---|---|
| M1 | Per-target blending in `[PickBlendMode]` | Additive sprites over a mesh no longer change normal / velocity buffers (bridge readback); TXAA with velocity unchanged on opaque scenes; both backends, Vulkan validation clean |
| M2 | `Light` struct + spot and directional in `ComputePbr()`, new ops | Existing point-light projects render identically (screenshot diff); spot cone and directional match expectations; non-PBR shaders still compile |
| M3 | Material targets in `[RenderTarget]` / bundle, written by the 6 PBR shaders | Buffers show expected values for a known material; MSAA on/off; slots correct on Vulkan with only some targets enabled |
| M4 | SDF `p.w = 2` pass + `[SetSDFMaterial]` inputs | Two blended SDFs with different roughness transition smoothly; projects without `[SetSDFMaterial]` unchanged |
| M5 | SSR uses roughness / metal | Rough vs. polished surfaces show blurred vs. sharp reflections |
| M6 | Area lights (sphere/tube, then rect LTC) | Compare against a reference (e.g. Blender Eevee) for one setup |
| M7 | `[DeferredLights]` op | Same light forward vs. deferred on an opaque scene matches within tolerance |

Each milestone gets a `.tests-manual/` set and `.help/` updates for the new ops.

## Open Questions

1. Alpha-blended (Normal mode) draws: write normal/material (top surface wins, matches today's normal behaviour) or
   mask them out (SSR/relight sees the surface behind)? Proposal: write, since alpha cut-off is the common use.
2. Light ops: one `[Light]` op with a type enum, or separate ops per type? Separate ops give cleaner parameter sets.
3. Light count: keep 8 in M2, or move to a structured buffer right away?
4. Base color for vertex-color / point-color driven draws: store the multiplied color (proposal) or the material only?
5. Should `[DrawMeshCelShading]` / hatched get spot and directional in M2 or later?

## Risks

- Five targets at 4K with MSAA: memory and bandwidth; opt-in keeps the default cost at zero.
- Light struct layout change touches C# and every shader reading lights; keep offsets of existing fields stable.
- Shaders writing SV_Target3/4 while those targets are unbound: fine on D3D11; check Vulkan validation (precedent:
  draws already write SV_Target1/2 without bound targets).
- Changing `[PickBlendMode]` affects 51 symbols; M1 must compare before/after across the example projects.
