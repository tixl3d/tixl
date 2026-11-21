# MeshToSDF

*in [Lib.field.generate.sdf](README.md)*

Bakes a signed distance field volume from a triangle mesh.

The mesh is voxelized on the GPU and the volume is filled with nearest-surface distances using jump flooding (port of Fire-Aalt mesh-to-sdf). Inside the mesh the distance is negative.

Connect the result to [RaymarchField] to render the mesh as a raymarched SDF, or combine it with other SDF primitives (e.g. [CombineSDF], [BendField], [PolarRepeat]).

With AutoFitVolume enabled (default) the bake volume automatically fits the mesh bounding box, so the whole mesh is always captured. Disable it to place the box manually with Center and Size. Resolution sets the voxels along the largest axis.

With AutoRebake enabled the volume is re-baked whenever the mesh changes. Bakes are split into work units that are spread over BakeFrames frames, so even heavy bakes never freeze the app: [SdfTexture] always holds the last completed bake while [BakeProgress] (0..1) and [BakeInProgress] report the progress of the running one, and [BakeCount] increments when it finishes. Set BakeFrames to 1 to force the whole bake into a single frame.

Surface quality guide: with Smoothing at 0 the raw voxel field shows staircase and speckle. For clean raymarched surfaces use Resolution 256, SampleDensity 8-16 (low values leave holes in thin parts, which shows as speckle), and Smoothing 0.4-0.6 with a SmoothingRadius of 2-3 to melt the voxel noise. Mode Exact Closest Point Scatter gives the smoothest surfaces (exact triangle distances near the mesh).

Invert flips inside and outside - useful for hollow shells, rooms or carving. Smoothing can be tweaked after a bake without re-baking it. The [SdfTexture] output can be inspected with [ShowTexture3d].

Limits and failure behaviour: Resolution is capped at 256, which uses about 500 MB of GPU memory (about 63 MB at 128 and 8 MB at 64). If that allocation fails, the bake reports it in the status bar, releases the partial allocation and waits for a changed input before retrying. Meshes above about 4.2 million triangles exceed the GPU dispatch limit and are rejected with a message instead of being baked. A failed bake never retries in a loop, never blocks the app and never crashes it - change an input or pulse Bake to try again.

## Robustness

The operator is written to be safe to leave in a live composition:

- **Thread safe.** All shared state and GPU work is serialized through one state lock, and the per-frame guard sits inside it so two evaluation threads cannot start duplicate bakes. The status bar reads progress lock-free (volatile fields) so the UI never waits behind a running bake step.
- **Memory safe.** Volume allocation is all-or-nothing: if one of the seven volumes cannot be allocated the partial set is released and the bake stays retryable. All numeric inputs are sanitized against NaN and Infinity before they reach the constant buffer, the face count is validated against the D3D11 dispatch cap, and the only CPU readback is a fixed 24-byte bounds struct.
- **Non-blocking.** A bake is a queue of GPU work units spread over BakeFrames frames. The published volume is only replaced by a single copy after the bake completes, so downstream rendering always samples a complete field.
- **Unkillable.** Every queue step is wrapped in exception handling that aborts the bake, unbinds the compute stage and reports through the status bar. Every shader loop has a compile-time or buffer-derived bound, so no input can produce an unbounded GPU loop. The exact closest-point mode is split into row bands sized so a single dispatch cannot outrun the OS GPU watchdog (which would reset the device and take the application with it). After a failure the bake only retries once an input actually changes.

Peak GPU memory for the working volumes (seven volumes, 30 bytes per voxel): ~8 MB at Resolution 64, ~63 MB at 128, ~500 MB at 256.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers Required) | The triangle mesh to convert. Vertex buffers of mesh operators connect here. |
| **Resolution** (Single) | Voxels along the largest volume axis. Higher values capture finer detail at the cost of memory and bake time (256 uses roughly 500 MB of GPU memory across the working volumes). 128 is a safe default, 256 for close-up surfaces. |
| **Center** (Vector3) | Center of the baked volume box in local space. Only used when AutoFitVolume is off. |
| **Size** (Vector3) | Size of the baked volume box in local space. Only used when AutoFitVolume is off; the mesh must fit inside this box to be captured fully. |
| **AutoFitVolume** (Boolean) | Fits the bake volume to the mesh bounding box on every bake, so the whole mesh is always captured. When disabled, the volume box is placed manually with Center and Size. |
| **SampleDensity** (Single) | Sample points per voxel² of triangle area used during voxelization. Low values can leave holes in thin parts, which shows as speckle in the field. Use 8-16 for detailed meshes. |
| **Mode** (Int32) | Conversion algorithm: 0 = Jump Flood (fast, signed - good default), 1 = Exact Closest Point (slow brute force per voxel), 2 = Exact Closest Point Scatter (GPU-parallel, exact distances near the surface - smoothest result), 3 = Jump Flood unsigned (distance only, always positive). |
| **Smoothing** (Single) | Blurs the distance field to remove voxel noise (0 = off). 0.4-0.6 gives clean raymarched surfaces. Changing this after a bake re-applies smoothing without a full re-bake. |
| **SmoothingRadius** (Single) | Radius of the smoothing kernel in voxels. Values below 1 barely smooth; 2-3 works well at 128-256 resolution. |
| **BakeFrames** (Single) | Number of frames a bake is spread over so the app stays responsive. 1 = bake within a single frame (can cause a hitch on large volumes). Changing this only affects future bakes. |
| **Invert** (Boolean) | Flips inside and outside of the field, turning the mesh into a hollow shell. Applied during the next bake. |
| **Offset** (Single) | Shifts the resulting surface inward (negative) or outward (positive) by this many world units. |
| **Bake** (Boolean) | Pulse to rebake the volume with the current settings. |
| **AutoRebake** (Boolean) | Rebake automatically whenever the mesh changes. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |
| **SdfTexture** | T3.Core.DataTypes.Texture3dWithViews |
| **BakeCount** | System.Single |
| **BakeProgress** | System.Single |
| **BakeInProgress** | System.Boolean |

