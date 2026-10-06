# Upgrading projects to the Vulkan renderer

TiXL used to render with Direct3D 11 and compile shaders with Microsoft's FXC. It now renders with Vulkan and compiles with Slang. If you have been using TiXL for years and have a folder full of projects and custom shaders, this page tells you what to expect, what TiXL translates for you, and the handful of things you may have to fix by hand.

## The short answer

Your projects open as before. The `.t3` project format did not change, operators did not change, and your parameters, animations and presets are untouched.

Most shaders compile unchanged too, because TiXL passes the compiler a set of compatibility flags described below. What is left falls into two groups, and the second is the one to watch:

- **It does not compile.** Slang rejects some things FXC quietly accepted. You get an error, you fix it, you move on.
- **It compiles but looks different.** A few constructs were always undefined, and FXC happened to do what you wanted. These are silent, and they are the ones worth reading this page for.

## What actually changed

Four names get used interchangeably in bug reports, so it helps to separate them:

| | Before | Now |
|---|---|---|
| Graphics API | Direct3D 11 | Vulkan |
| Shader language | HLSL | HLSL — unchanged |
| Shader compiler | FXC | Slang |
| Compiled shader format | DXBC | SPIR-V |

The important line is the second one. **You still write HLSL.** Slang is a superset of it, so the language you know is the language you keep using. What changed is the program that reads your HLSL and the bytecode it produces.

Vulkan is the reason for the move: it runs on Linux and macOS, where Direct3D does not. SPIR-V is simply what Vulkan consumes instead of DXBC, and Slang is the compiler that produces it.

## How TiXL compiles your shaders

TiXL runs `slangc` once per shader, roughly like this:

```bash
slangc your-shader.hlsl \
  -entry psMain -stage pixel \
  -target spirv -profile sm_5_0 -capability spirv_1_5 \
  -D sampler=SamplerState \
  -fvk-use-dx-layout \
  -fvk-s-shift <n> 0  -fvk-b-shift <n> 0 \
  -fvk-t-shift <n> 0  -fvk-u-shift <n> 0 \
  -I <include directories>
```

Each of those flags exists to keep your existing shaders working:

- **`-D sampler=SamplerState`** maps the D3D9-era `sampler` keyword to the modern type. FXC accepted `sampler`; Slang does not. Around 225 of TiXL's own shaders still use it, so this mapping is permanent — you do not need to rewrite `sampler texSampler : register(s0);`.
- **`-fvk-use-dx-layout`** keeps D3D's constant-buffer packing rules. Your `cbuffer` members sit at the same offsets they always did, which matters because the C# side of an operator writes those bytes.
- **`-fvk-*-shift`** maps HLSL register slots onto Vulkan descriptor bindings. Vulkan has one binding space where D3D had four separate ones, so each register class gets its own range: samplers from 0, constant buffers from 16, textures and other shader resources from 32, and UAVs from 160. Each stage is offset by a further 200, so a vertex shader's `t0` and a pixel shader's `t0` do not collide.
- **`-profile sm_5_0`** keeps the shader model your shaders were written against.

The practical consequence: **`register(t0)`, `register(b0)` and `register(s0)` still mean what they meant.** Keep writing them.

## Errors you may actually hit

### Mixing vector sizes

This is the most common hard failure.

```hlsl
float4 p = ...;
float3 center = ...;
float d = length(p - center);   // error
```

```
error[E39999]: no overload for '-' applicable to arguments of type (vector<float,4>, vector<float,3>)
```

FXC silently truncated the `float4` to a `float3` and carried on. Slang refuses to guess. Say which components you meant:

```hlsl
float d = length(p.xyz - center);
```

This one bit TiXL's own SDF operators during the port, so it is worth grepping your shaders for arithmetic between differently sized vectors.

### "Undefined identifier" on a type name

If Slang reports an undefined identifier where you used a type, it is usually a D3D9-era name that the compatibility define does not cover. `sampler` is handled; others are not. Replace it with the modern spelling — `SamplerState`, `Texture2D`, and so on.

## Differences that compile but change the picture

These produce no error at all. If a shader went black or subtly wrong after the upgrade, start here.

### Uninitialised struct members

```hlsl
struct Fragment { float3 color; float fog; };

Fragment f;
f.color = albedo;          // f.fog is never written
return float4(f.color * (1 - f.fog), 1);
```

Slang accepts this. The value of `f.fog` is undefined, and in practice it can come through as NaN — which propagates through every multiply and turns the whole surface black. FXC frequently left such members as zero, so the bug never showed.

Initialise the struct:

```hlsl
Fragment f = (Fragment)0;
```

This was a real bug in TiXL's own PBR shading during the port, and it cost a day to find because nothing reports it.

### smoothstep with a descending edge

```hlsl
smoothstep(1.0, 0.0, x)     // compiles; result is not what you want
```

`smoothstep` is only defined when `edge0 < edge1`. FXC's output happened to be useful when the edges were swapped. Write the inversion explicitly instead:

```hlsl
1.0 - smoothstep(0.0, 1.0, x)
```

### A function named after an intrinsic

If you define your own `fmod`, `saturate` or similar, Slang resolves the call to **your** function. That may or may not be what the old build did, and the two compilers are not obliged to agree. The failure is silent and looks like "this function suddenly behaves differently".

Rename your version — `myFmod`, `wrapMod` — and the ambiguity disappears.

### Implicit conversions now warn

```hlsl
float left = uv.x < Left;   // warning: implicit conversion from 'bool' to 'float'
```

These still compile and still do what you expect. Slang warns because the intent is easy to misread, not because the result changed. You can leave them alone, or be explicit:

```hlsl
float left = uv.x < Left ? 1.0 : 0.0;
```

## When a shader fails to compile

The compiler's message appears in the log window, with the file, line and column. Slang's diagnostics are more precise than FXC's — they point at the exact expression, and they explain the overload that was not found rather than just saying the call failed.

For a generated shader — anything TiXL assembles from a field graph — there is no file on disk to open. TiXL therefore writes the exact source it handed the compiler into a `Tmp/ShaderErrors` folder inside its settings folder, named after the shader and the entry point. Open that file to see the code the error line numbers refer to, and compile it by hand if you want to iterate quickly:

```bash
slangc ShaderErrors/PixelShader_psMain.01.hlsl -entry psMain -stage pixel -target spirv -o /tmp/out.spv
```

## See also

- [Using custom shader operators](UsingCustomShaders.md)
- [A shader development example](ShaderDevelopmentExample.md)
- [Converting raymarching functions](ConvertSDFs.md)
