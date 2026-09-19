# Graphics facade: the API that replaces SharpDX

**Status:** Draft — 2026-09-20. Nothing implemented.
**Belongs to:** [Plan_CrossPlatformV5](Plan_CrossPlatformV5.md), Phase 3. Read its Key decisions first.

This specifies the API of the new `Graphics/` project, which every operator and the editor call instead of
SharpDX. The Vulkan backend is written against it later. The plan calls this API permanent — it *is* TiXL's
operator model — so it is worth reviewing as text before ~150 files are rewritten onto it.

## What this has to be

1. **A D3D11-shaped API, not a new render API.** The 44 `_dx11` operators hand the D3D11 state machine to
   users and everything else is built from them. Users' projects and their custom C# operators keep working.
2. **Small.** Only what TiXL calls today, plus what the Vulkan backend needs to be told (see
   [Additions](#additions-the-backend-needs)). Every type below is in the repository today.
3. **Mechanically portable.** Ops should migrate by swapping `using` lines and a handful of renames.
4. **Behaviour-compatible.** The compatibility contract below matters more than the signatures: TiXL relies on
   D3D11 leniency in places, and the backend has to reproduce it rather than reject it.

## Naming and layout

- Namespace `T3.Graphics` in project `Graphics/` (`net10.0`), with backends `Graphics.D3D11/` and
  `Graphics.Vulkan/`.
- Type names stay identical to SharpDX's (`Texture2D`, `ShaderResourceView`, `SamplerStateDescription`, …),
  so ops migrate by swapping `using SharpDX.Direct3D11;` for `using T3.Graphics;`.
- TiXL's existing wrapper types (`T3.Core.DataTypes.Texture2D`, `BufferWithViews`, the shader types) move into
  `Graphics/` and keep their namespaces, so operator code that uses the aliases does not change at all.
- Enums are copied from SharpDX's MIT-licensed source with identical member names **and values**: projects
  store them by name, and shaders and C# structs assume the values.

## The API

### Device — resource creation, callable from any thread

Constructors TiXL uses today (counts from the repository):

| Type | Uses | Overloads needed |
|---|---|---|
| `ShaderResourceView` | 42 | default (null desc), `ShaderResourceViewDescription` (Texture2D and ExtendedBuffer forms) |
| `Texture2D` | 34 | `Texture2DDescription`, description + initial data |
| `Buffer` | 23 | 7-argument form, `BufferDescription`, description + `DataStream` |
| `RenderTargetView` | 22 | default, `RenderTargetViewDescription` (Texture2DArray form) |
| `UnorderedAccessView` | 13 | default, `UnorderedAccessViewDescription` (Buffer form) |
| `RasterizerState` | 7 | `RasterizerStateDescription` |
| `SamplerState` | 5 | `SamplerStateDescription` |
| `BlendState` | 4 | `BlendStateDescription` |
| `Texture3D`, `Texture1D`, `DepthStencilView`, `DepthStencilState` | 3 each | description forms |
| `VertexBufferBinding`, `Query`, `InputLayout` | 1–2 | as today |

Shaders are created through `ShaderCompiler` (see [Shaders](#shaders)), not by operators.

### DeviceContext — the immediate context, main thread only

Verbs actually called (counts from the repository):

- **Draw and dispatch:** `Draw` (20), `DrawIndexed`, `DrawInstanced`, `DrawIndexedInstanced`,
  `DrawInstancedIndirect` (4), `Dispatch` (11), `DispatchIndirect` (1).
- **Copy and update:** `CopyResource` (32), `CopySubresourceRegion` (3), `UpdateSubresource` (25),
  `CopyStructureCount` (1), `ResolveSubresource` (4), `GenerateMips` (5).
- **Map:** `MapSubresource` (33) / `UnmapSubresource` (46).
- **Clear:** `ClearRenderTargetView` (12), `ClearDepthStencilView` (3), `ClearState`.
- **Queries:** `Begin` / `End` / `GetData` (used by `GpuMeasure`), `Flush`, `ExecuteCommandList`.

Stage objects and their members, from the `_dx11` operators (the authority on what must exist):

| Stage | Set | Get (see [state readback](#state-readback)) |
|---|---|---|
| `InputAssembler` | `PrimitiveTopology`, `SetVertexBuffers`, `SetIndexBuffer` | `GetIndexBuffer`, `GetVertexBuffers` |
| `Rasterizer` | `State`, `SetViewport(s)`, `SetScissorRectangle(s)` | `GetViewports<T>`, `State`, `GetScissorRectangles` |
| `OutputMerger` | `SetTargets`, `SetRenderTargets`, `SetUnorderedAccessViews`, `BlendState`, `SetBlendState`, `SetDepthStencilState` | `GetRenderTargets`, `GetBlendState` |
| `VertexShader`, `PixelShader` | `Set`, `SetConstantBuffer(s)`, `SetShaderResource(s)`, `SetSampler(s)` | `Get`, `GetConstantBuffers`, `GetShaderResources`, `GetSamplers` |
| `GeometryShader`, `HullShader`, `DomainShader` | `Set` (+ the same setters) | `Get` |
| `ComputeShader` | `Set`, `SetConstantBuffer(s)`, `SetShaderResource(s)`, `SetSampler(s)`, `SetUnorderedAccessViews` | `Get` |

### State readback

`_dx11` operators implement TiXL's save/restore protocol by *reading D3D state back* and restoring it later
(`OutputMergerStage`, `Rasterizer`, `SetPixelAndVertexShaderStage`, `Draw`). The set and the restore usually
happen in different operators: an operator's `Update` sets state, and its `Command.RestoreAction` runs later
in the enclosing operator (`Execute`, `Group`, `Switch`, `TimeClip`, `SpreadIntoGrid`).

Vulkan has no state to read back, so the facade keeps a **shadow state stack** in the `Graphics` layer, and
the operators use an explicit **push/pop API** instead of SharpDX's `Get*` calls (decision 2). The state
operators are rewritten by hand; the protocol stays the same (an operator pushes in `Update`, the enclosing
operator pops through `Command.RestoreAction`), but what is saved and restored becomes explicit instead of
being whatever the driver reported.

This also removes three current flaws: the readback in `Draw` and `SetPixelAndVertexShaderStage` leaks
AddRef'd COM objects; `SetPixelAndVertexShaderStage` reads previous state from the vertex stage and applies it
to both stages; its appended `_additionalSrvs` are never unbound. Slot counts, which today come unclamped from
multi-input array lengths, are clamped to the D3D11 limits and logged once (decision 4).

### Resources, views and descriptions

Description structs keep SharpDX's names and fields. Fields TiXL never sets keep D3D11's defaults. The
`_dx11` operators and `Gfx` state operators write these fields, and the facade must accept all of them:

- `Texture2DDescription`, `Texture3DDescription`: `Width`, `Height`, `Depth`, `ArraySize`, `BindFlags`,
  `Usage`, `CpuAccessFlags`, `Format`, `MipLevels`, `OptionFlags`, `SampleDescription`.
- `BufferDescription`: `Usage`, `BindFlags`, `SizeInBytes`, `OptionFlags`, `StructureByteStride`,
  `CpuAccessFlags`.
- `SamplerStateDescription`: `Filter`, `AddressU/V/W`, `MipLodBias`, `MaximumAnisotropy`,
  `ComparisonFunction`, `MinimumLod`, `MaximumLod` (`BorderColor` is deliberately unused today; Vulkan needs
  `customBorderColor` if it ever is).
- `RasterizerStateDescription`: all ten fields.
- `BlendStateDescription`: `AlphaToCoverageEnable`, `IndependentBlendEnable`, `RenderTarget[8]`.
- `DepthStencilStateDescription`: `IsDepthEnabled`, `DepthWriteMask`, `DepthComparison` — no stencil fields
  are used today.
- View descriptions: `ShaderResourceViewDescription` (Texture2D, ExtendedBuffer), `RenderTargetViewDescription`
  (Texture2DArray), `DepthStencilViewDescription` (Texture2D), `UnorderedAccessViewDescription` (Buffer).

Members read back from resources, which the facade must expose as getters: `Description` on textures, buffers
and views, `Buffer.ElementCount` on SRV/UAV descriptions, `IsDisposed`, `DebugName` (setter), and resource
identity for view caching (today `Resource.NativePointer`; the facade replaces it with a comparable handle).

### Enums stored in projects

These are saved by name in `.t3` files and must keep their member names and values. Counts are occurrences in
the repository's projects:

| Enum | Saved uses | Members seen |
|---|---|---|
| `TextureAddressMode` | 447 | `Wrap`, `Clamp`, `Mirror`, `MirrorOnce`, `Border` |
| `CullMode` | 150 | `None`, `Front`, `Back` |
| `Format` (DXGI) | 102 | 9 members saved; ~26 used in code; the UI offers the whole enum, so copy it completely |
| `BlendOption` | 65 | `Zero`, `One`, `SourceColor`, `InverseSourceColor`, `SourceAlpha`, `InverseSourceAlpha`, `DestinationAlpha`, `InverseDestinationColor` |
| `Filter` | 55 | `MinMagMipPoint`, `MinLinearMagMipPoint`, `MinMagMipLinear`, `Anisotropic`, `ComparisonMinMagMipPoint` |
| `BindFlags` | 38 | `ShaderResource` (flags enum; all members needed) |
| `Comparison` | 11 | `Always`, `LessEqual`, `Less` |
| `BlendOperation` | 7 | `Add`, `Minimum`, `ReverseSubtract` |
| `ResourceOptionFlags` | 5 | `None`, `GenerateMipMaps`, `BufferStructured`, `TextureCube` |
| `UnorderedAccessViewBufferFlags` | 1 | `None`, `RawData`, `Append`, `Counter` |

Also exposed as operator input types: `PrimitiveTopology`, `FillMode`, `ResourceUsage`, `CpuAccessFlags`,
`ColorWriteMaskFlags`, `DepthWriteMask`. Some operators map an `int` input onto an enum via `MappedType`
(e.g. `SharedEnums.BlendModes`), which keeps working unchanged.

### Interop value types

`SampleDescription` gets a facade equivalent. `RawColor4`, `RawViewportF` and `RawRectangle` do **not**: those
operator slots move to `System.Numerics` types (decision 3), which needs a migration step for saved values and
new type-registry entries — the short registered names are persisted in DataSet channels, so the old names
must keep resolving. `DataStream` (23 files), `DataBox` (25) and `DataRectangle` (11) get replacements;
`SharpDX.Utilities.Dispose` gets a small helper.

### Swapchain and presentation

Created from an SDL window, not from an HWND: the D3D11 backend pulls the HWND out of the SDL window, the
Vulkan backend creates a `VkSurfaceKHR`. The API covers what the editor and Player use today:

- description: buffer count (2–3), format, `FlipDiscard`-equivalent, `AllowModeSwitch`
- `ResizeBuffers`, back-buffer access as a `Texture2D`, `Present(syncInterval)`
- **frame pacing:** the editor creates a waitable swapchain (`FrameLatencyWaitAbleObject`) and waits on it
  once per frame (`AppWindow.WaitForFrameLatency`). The facade keeps a `WaitForFrameLatency()` on the
  swapchain; Vulkan implements it with its frames-in-flight fences.

### Shaders

Operators hold `T3.Core.DataTypes.VertexShader` / `PixelShader` / `ComputeShader` / `GeometryShader` wrappers
and never create them directly; `ShaderCompiler` does (Slang by then). The facade only needs the shader
objects, their `DebugName`, and `ComputeShader.TryGetThreadGroups` — the single piece of shader reflection in
the codebase, which Slang's reflection answers.

### Wrapper types and what they expose

TiXL's own wrappers move into `Graphics/` unchanged in shape, because operator code uses them everywhere:

- `Texture2D` / `Texture3D` (`Core/DataTypes/Texture.cs`) wrap the native texture and carry a **snapshot copy**
  of the description in a public readonly field. Dozens of call sites branch on `texture.Description.Width` /
  `Height` / `Format`, so the facade keeps a snapshot rather than a live query.
- `Texture<T>` has implicit conversions to the native type. In the facade these become conversions to the
  facade type, which is what makes the codemod cheap.
- `IsDisposed` is polled all over the operator code and must stay.
- `BufferWithViews`, `Texture3dWithViews`, `MeshBuffers`, `RenderTargetReference` hold views in public mutable
  fields; `MeshBuffers.FaceCount` reads `Srv.Description.Buffer.ElementCount`.
- View-creating extensions (`CreateShaderResourceView` / `RenderTargetView` / `UnorderedAccessView`) dispose
  the old view, create the default view and set `DebugName`.

Two things to fix while moving them, since Vulkan makes both worse:

- `AbstractTexture` and `AbstractShader` have **finalizers that release GPU objects**, so resources can be
  freed on the GC thread while the render loop runs. With deferred destruction the finalizer should only
  hand the resource to the backend's destruction queue.
- `Texture3dWithViews` is not disposable at all and leaks by construction.

### Threading

The facade must state which calls are thread-safe, because today's code already creates resources off the
render thread:

- **Thumbnails** (`ThumbnailManager.LoadTextureViaWic`) create a texture with initial data on a thread-pool
  thread; `VideoClipThumbnailCache` does the same on its worker.
- **Video decoding** hands `Device.NativePointer` to FFmpeg, which creates decoder surfaces on its own
  thread. `HardwareFrameConverter` wraps those surfaces, guarded by its own device lock.
- **Spout** passes a texture's native pointer to its interop layer.
- **`StallWatchdog`** runs its own thread with a **deferred context**, records a command list, and executes
  and presents it from that thread. It enables D3D11's multithread protection for this.

So the facade needs: resource creation callable from any thread, **adopting an externally created texture**
from a native handle (FFmpeg, Spout), and an explicit statement about the immediate context being main-thread
only. The deferred-context path of the watchdog is the one place that needs a Vulkan answer of its own — a
second command buffer, or dropping the watchdog's GPU work.

### Lifetime

Today there is **no deferred destruction and no reference counting**: every `Dispose()` releases immediately,
and operators dispose and recreate views per frame. It works because the immediate context is the only
consumer. On Vulkan that model breaks, so the facade owns the rule:

- `Dispose()` marks a resource unusable for new work and hands it to the backend, which frees it after the
  frames in flight that may reference it have retired.
- `Resource<T>` (the file-backed resource with hot reload) may dispose a shader or texture in the middle of
  graph evaluation; deferred destruction covers that too.
- ImGui texture ids are the sharpest case: `ImGui.Image(srv.NativePointer, …)` hands a **raw pointer with no
  reference taken** into a draw list that is consumed a frame later, and `SrvManager.RemoveForDisposedTextures`
  runs before drawing. The facade's `ImGuiTextureId` must therefore be a handle the renderer resolves at draw
  time, with a defined result for a texture that died in between (draw nothing).

### CPU access

- **Upload:** `Map(WriteDiscard)` on dynamic buffers and textures, and `UpdateSubresource`. `ResourceUtils`
  deliberately avoids the `DataStream`-returning overloads because they dominated allocation in loops; the
  facade's upload API takes a span or a pointer.
- **Readback, async:** `TextureReadAccess`, `TextureBgraReadAccess` and `StructuredBufferReadAccess` keep a
  3-deep staging ring, copy, and map two frames later. This is the shape the facade blesses.
- **Readback, blocking:** several operators (`PickColorFromImage`, `ReadPointColors`, `ReadPointsFromGpu`,
  the Mediapipe family, video export, the visual tests) copy, `Flush()` and map in the same frame. This must
  keep working, including the stall.
- `DataStream`, `DataBox` and `DataRectangle` appear in public Core signatures
  (`Texture2D.CreateTexture2D(description, DataRectangle[])`, `ResourceManager.SetupStructuredBuffer(DataStream …)`,
  the readback callbacks). Their replacements keep the same call shapes so the codemod stays mechanical.

### Type registration

`SymbolPackage.TypeRegistration.cs` registers every D3D11 type as an operator input type with a short name
(`"SamplerState"`, `"Format"`, `"TextureAddressMode"`, …). Two different persisted forms exist, and only one
is safe to change:

- The **short registered names** are persisted in DataSet channels and shown in the type picker. They must
  stay exactly as they are.
- The **full .NET type names** in `.t3` input values change with the namespace; the reader ignores them.

### Additions the backend needs

Four things the Vulkan backend cannot infer, kept out of the operators' way:

1. **`ImGuiTextureId` on `ShaderResourceView`**, replacing `NativePointer` as the ImGui texture id (18 files).
2. **Async readback.** `TextureBgraReadAccess` already queues a copy to a staging texture and calls back when
   the data is ready (screenshots, video export, visual tests). It becomes part of the facade API, and
   `Map(Read)` on the immediate context stays as the blocking fallback.
3. **Frame boundary.** The backend needs to know when a frame begins and ends for the upload ring, deferred
   destruction and pipeline batching. The render loops call it; operators do not.
4. **Debug names** map to Vulkan debug names so resources show up by name in RenderDoc.
5. **Memory pressure.** Resource creation can fail on Vulkan, which it effectively never did on D3D11. The
   facade returns null for a failed resource (operators already handle null outputs) and raises a
   memory-pressure event the editor answers by dropping rebuildable caches. It also owns the pool for
   transient render targets, since operators create and drop them every frame.
6. **Adopting a foreign texture** from a native handle, for FFmpeg's decoder surfaces and Spout. On Vulkan
   these become external-memory imports, and both features are out of scope for v5.0 — but the API shape has
   to leave room for them.

## Compatibility contract

The backend must reproduce these behaviours; operators depend on them today, mostly without knowing it.

| Behaviour | Where it comes from | Vulkan answer |
|---|---|---|
| Binding a disposed or null view unbinds the slot and never crashes | SharpDX turns disposed objects into null pointers; ops poll `IsDisposed` and pass nulls (`OutputMergerStage`, `DrawInstancedIndirect` unbinds with an array of 4 nulls) | `nullDescriptor` from `robustness2`; the facade maps disposed handles to null |
| Out-of-bounds buffer reads return 0, writes are dropped | shaders rely on it | `robustness2` |
| Writes of one draw or dispatch are visible to the next | D3D11 auto-hazard tracking | automatic barriers in the backend |
| State persists between operators and is restored later by another operator | `Command.PrepareAction` / `RestoreAction` protocol | shadow state stack (above) |
| `Map(WriteDiscard)` and `UpdateSubresource` never stall and never break a pass | per-frame constant buffer updates (`FloatsToBuffer` maps every update) | per-frame upload ring ("renaming") |
| `Map(Read)` blocks until the GPU is done | screenshots, visual tests | same, plus the async API above |
| Resources can be created on any thread while the context stays main-thread only | thumbnails, video decoding, texture loading | Vulkan resource creation is thread-safe; command recording is not |
| `Dispose()` is safe at any time; another operator may still reference the resource this frame | ops dispose and recreate views per frame | deferred destruction after the frames in flight retire |
| A state object created per frame is cheap | `Gfx` state ops recreate blend/raster/sampler states whenever dirty | cache by description hash in the backend |

## Migration

- **By hand, not by codemod:** the ~10 state operators (push/pop API) and the operator slots that move to
  `System.Numerics`, with their migration step.
- **Codemod** across ~131 files that import SharpDX: swap `using` lines, rewrite fully-qualified names,
  `NativePointer` → `ImGuiTextureId`, `DataStream`/`DataBox` → the replacements. The aliases in
  `Lib.GlobalUsings.g.cs` mean most operator files change by zero lines.
- **`.t3` files:** the `"Type"` strings change (236 files). The reader ignores them, so the change is safe;
  re-save all library symbols in one commit to avoid diff noise.
- **User projects:** a migration step rewrites `using SharpDX.*` in user operator code and drops the SharpDX
  package references. Users who wrote custom shaders or operators against SharpDX types need the same rename;
  document it in the v5 migration notes.
- **Order:** `Graphics` + `Graphics.D3D11` first, with the visual suite green and metrics unchanged, before
  any Vulkan work depends on it.

## Decisions (2026-09-20)

1. **Namespace and assembly: `T3.Graphics`**, project `Graphics/`, backends `Graphics.D3D11/` and
   `Graphics.Vulkan/`. Operator files swap one `using` line.
2. **State save/restore becomes an explicit push/pop API**, backed by the shadow state stack. The SharpDX
   `Get*` shape is not kept. Consequences: the ~10 state operators (`OutputMergerStage`, `Rasterizer`,
   `SetPixelAndVertexShaderStage`, `InputAssemblerStage`, `Draw`, the `Gfx` state ops) are rewritten by hand
   rather than by the codemod, and today's asymmetric restores (previous state read from the vertex stage and
   applied to both stages; `_additionalSrvs` never unbound) are fixed in the process. A user operator that
   does its own save/restore needs the same change — document it in the migration notes.
3. **Operator slots move to `System.Numerics`**: `RawViewportF`, `RawRectangle` and `RawColor4` disappear from
   the facade's public surface. Consequences: a migration step for saved values, new entries in the type
   registry (the short registered names are persisted in DataSet channels, so the old names must keep
   resolving), and the type picker shows the new types. Worth doing once, while the codemod is touching these
   files anyway.
4. **Binding counts are clamped to the D3D11 limits and logged once** per operator, instead of letting a
   quiet D3D11 misuse become a hard Vulkan failure.
5. **Vulkan bindings: decided after the vertical slice.** The spike stays on Vortice.Vulkan 3.2.3; revisit
   once the slice has exercised descriptors, barriers and VMA.
6. **`Texture1D` stays** in the facade.
7. **`StallWatchdog` keeps drawing**: the facade exposes a secondary recording context (a second command
   buffer and pool on Vulkan), so the "application is frozen" overlay survives the port.
8. **`ICalibrationOverlay.Draw`** takes the facade's `DeviceContext` — a mechanical rename, no new
   abstraction.

## Still open

- Whether the push/pop API is scope-based (`using var scope = context.PushState()`) or paired calls. Decide
  when the first state operator is rewritten.
- Which `System.Numerics` type replaces `RawViewportF` (a `struct Viewport` in the facade, or six floats on
  the slot). Decide with the migration step.
