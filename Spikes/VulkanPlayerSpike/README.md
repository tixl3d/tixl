# Vulkan player spike

Throwaway proof that the v5 stack works on Linux before the real code moves over: SDL3 window, Vulkan 1.3,
and TiXL's own HLSL compiled to SPIR-V by Slang. Not part of the build's product output; see
[Plan_CrossPlatformV5](../../.agentic/Plans/Plan_CrossPlatformV5.md).

## What it renders

The image-effect chain TiXL builds from operators, here by hand
(`ImageEffectChain`), mirroring `_ImageFxShaderSetup2`:

1. `img/generate/MandelbrotFractal.hlsl` into a half-float render target (`[RenderTarget]`).
2. `img/fx/Sharpen.hlsl` samples that target into the swapchain (`[SrvFromTexture2d]` + the effect operator).

Both shaders are Lib's, unmodified. Constants are written per frame into a uniform ring (`UniformRing`), the
way `[FloatsToBuffer]` rewrites its constant buffer every update; resources are bound by the names the HLSL
declares, from Slang's reflection.

## Running it

```
dotnet run --project Spikes/VulkanPlayerSpike            # Esc quits, F11 fullscreen
    --frames N              exit after N frames (exit code 2 if validation reported errors)
    --no-validation         skip the validation layer
    --effect <path> [--effect-entry <name>]   use another pixel shader (absolute paths work)
    --strength <float>      sharpen amount; 0 must reproduce the source image exactly
    --capture <file.raw>    render one frame off-screen and read it back
```

`python3 raw_to_png.py <file.raw>` turns a capture into a PNG (standard library only).

Debug hooks: `TIXL_NO_PUSH=1` uses a conventional descriptor set instead of push descriptors,
`TIXL_EFFECT_SPIRV=<file.spv>` runs a module from another compiler with Slang's reflection, and
`TIXL_SLANGC` overrides the compiler path.

## What it confirmed

- Constant buffers match D3D packing with `-fvk-use-dx-layout` — including a real operator shader whose
  parameters are written from a C# struct.
- Reflection-driven binding works: any image shader can be run without a hand-written binding table.
- A render target sampled by the next pass needs only the two layout transitions in `RenderTarget`.
- Readback of a rendered frame (the shape the visual tests need on Linux) works.
- The negative-viewport Y flip reproduces D3D's orientation through a multi-pass chain.

## Open issue: GetDimensions intermittently returns 0

`Texture2D.GetDimensions` — used by 227 shader files, including `Sharpen` — returns 0 on some runs on
RADV (Mesa 26.2, Strix Halo). The result is stable *within* a process and differs *between* processes.
When it returns 0, the sharpen filter's offsets become infinite and its output collapses (channels clamp to
0), which is what a user would see as a broken operator.

Established so far:

- The generated SPIR-V is correct (`OpImageQuerySizeLod` on the sampled image, extracting components 0 and 1).
- The same descriptor is sampled successfully in the same draw, so the image is bound.
- It happens with both the float and the uint overloads, and with a 256x1 texture as well as a large one.
- `spirv-val` passes and the validation layers report nothing.

Not yet known: whether it reproduces on another driver (NVIDIA, lavapipe) or with a descriptor set instead of
push descriptors (`TIXL_NO_PUSH=1` exists for that test but its descriptor-update path still produces
validation errors and needs fixing first). Until this is understood, treat it as a blocker for shaders that
query texture sizes — TiXL's own `Resolution` constant buffer is the obvious workaround, but that would mean
editing shaders.

Reproduction:

```
cat > /tmp/query.hlsl <<'EOF'
struct vsOutput { float4 position : SV_POSITION; float2 texCoord : TEXCOORD; };
Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);
float4 psMain(vsOutput input) : SV_TARGET
{
    float width, height;
    Image.GetDimensions(width, height);
    float v = width / 4096.0;
    return float4(v, v, v, 1);
}
EOF
dotnet run --project Spikes/VulkanPlayerSpike -- --frames 5 --effect /tmp/query.hlsl --capture /tmp/q.raw
python3 Spikes/VulkanPlayerSpike/raw_to_png.py /tmp/q.raw    # grey 179/255 means 2880, black means 0
```
