# Using Custom Shader Operators

Custom Shader Operators combine TiXL's preset system with its shader pipeline into a powerful and fun way to build your own effects. You write only the **body** of a shader function — the operator wraps it in a complete shader, compiles it on the fly, and shows errors directly in the editor.

As of v4.1 TiXL has:

- [CustomPixelShader] — compute a color for every pixel of a texture
- [CustomPointShader] — move, rotate, scale and color points in a point buffer
- [CustomForce] — apply custom forces to particles
- [CustomVertexShader] — displace and color the vertices of a mesh
- [CustomFaceShader] — manipulate mesh triangles, including breaking them apart
- [CustomSDF] — define a distance field that plugs into the field / raymarching pipeline

You don't need to write shader code to use them: open the **Variations** window, pick one of the presets and experiment with the parameters. Each op comes with a shared set of parameters — **Offset**, **A**, **B**, **C**, **D**, **GainAndBias** and a **Gradient** — that the shader fragment can read. Good presets include a comment explaining what each parameter does.

## How it works

Each operator owns a **template** shader file (the `TemplateFile` parameter — open it to see exactly what wraps your code). The template declares the constant buffers, textures and helper functions, then pastes your `ShaderCode` into the body of its main function:

```hlsl
// ...template sets up uv, c, p, v etc. ...
{
//- METHOD -------------------------------------
/*{method}*/          // ← your ShaderCode lands here
//----------------------------------------------
}
// ...template writes the expected variables back...
```

Your fragment works with **prepared local variables** and is expected to **set or modify specific output variables**. Everything else — thread dispatch, buffer reads and writes — is handled by the template.

The `AdditionalCode` / `AdditionalDefines` parameter is inserted at **global scope** above the main function. Use it for your own helper functions, constants, or declarations of additional resources.

## Required output variables

This is the contract per operator — what you get, and what you are expected to set:

| Operator             | Runs per...      | Prepared variables                                                       | You set / modify                                  |
| -------------------- | ---------------- | ------------------------------------------------------------------------ | ------------------------------------------------- |
| [CustomPixelShader]  | pixel            | `float2 uv`, `int2 PixelCoord`, `int2 TargetSize`                         | `float4 c` — the output color (defaults to white) |
| [CustomPointShader]  | point            | `Point p`, `uint idx` (alias `i`), `float f` = idx normalized to 0…1      | `p` — written to the result buffer                |
| [CustomForce]        | particle         | `Particle p` (read), `float3 vel`, `float3 pos`, `float4 col`, `float age`, `float2 uv` | `vel`, `col`, `pos` — blended back with **Amount** |
| [CustomVertexShader] | vertex           | `PbrVertex v`, `uint vertexIndex`                                         | `v` — written to the result mesh                  |
| [CustomFaceShader]   | triangle         | `PbrVertex v1, v2, v3`, `int3 faceIndices`, `uint faceIndex`, `float3 pos1, pos2, pos3` | `v1`, `v2`, `v3` — written as an unshared triangle |
| [CustomSDF]          | field sample     | `float3 p`, `float3 Offset`, `float A, B, C`                              | `return` the signed distance at `p`               |

The structs used above:

```hlsl
struct Point                  struct Particle               struct PbrVertex
{                             {                             {
    float3 Position;              float3 Position;              float3 Position;
    float FX1;                    float Radius;                 float3 Normal;
    float4 Rotation;  // quat     float4 Rotation;  // quat     float3 Tangent;
    float4 Color;                 float4 Color;                 float3 Bitangent;
    float3 Scale;                 float3 Velocity;              float2 TexCoord;
    float FX2;                    float BirthTime;              float2 TexCoord2;
};                            };                                float Selected;
                                                                float3 ColorRGB;
                                                            };
```

## CustomPixelShader

Runs a pixel shader over the target texture. The inputs **ImageA** and **ImageB** are bound as `Texture2D<float4>` (with the aliases `Image` and `Image2`), the **Gradient** as a 1-pixel-high lookup texture. Three samplers are available: `Sampler` (wrapped), `ClampedSampler`, and `CustomSampler` (from the **CustomSampler** input).

The default code is a simple vignette:

```hlsl
float d = 1 - length(uv - 0.5 - Offset * float2(1,-1));
d = ApplyGainAndBias(d, GainAndBias);

c = ImageA.Sample(Sampler, uv);
c.rgb *= SampleGradient(d).rgb;
```

Displacing one image with another:

```hlsl
// Connect two images and use the Offset parameter
float4 cb = ImageB.Sample(Sampler, uv);
float d = Biased(cb.r);
c = ImageA.Sample(Sampler, uv - d * Offset * float2(1,-1));
```

Additional inputs: use [FloatsToBuffer] / [IntsToBuffer] for extra constant buffers, [SrvFromTexture2d] for more textures, and [SamplerState] for custom samplers. Declare each of them in `AdditionalCode`, e.g. `Texture2D<float4> Image3 : register(t3);`.

## CustomPointShader

A compute shader running once per point of the connected buffer. `p` is a copy of the source point; whatever you leave in it is written to the output. `f` is the point's normalized position in the buffer (0…1) — ideal for distributing points along curves. `Time` holds the playback time scaled by **TimeScale**, and `TotalCount` the buffer size.

The default code moves points along their rotation and colors them with the gradient:

```hlsl
float t = Biased( frac(f * (B+1) + A) );
float3 pForward = qRotateVec3(float3(0,0,1), p.Rotation);

p.Position += pForward * (t - 0.5);
p.Position.y += sin(t * 2 * PI);
p.Color = SampleGradient(t);
p.Scale += t;
```

A grid of points forming a wave (from the **Wave** preset):

```hlsl
int gs = sqrt(TotalCount);
float2 gpos = float2(idx % gs, idx / gs) / gs - 0.5;

p.Position = float3(gpos.x, 0, gpos.y);
float h = length(sin(gpos * 8 + Offset.xz) * 0.1);
p.Position.y = h;
p.Color = SampleGradient(h * 8);
```

Generating points procedurally also works — set **Count** and leave the **Points** input empty; `p` then starts as a default point at the origin.

## CustomForce

Runs once per particle of a particle system. You modify the local copies `vel` (velocity), `col` (color) and `pos` (position); after your code they are blended into the particle with the **Amount** parameter. Avoid writing `pos` directly when you can express the change as a velocity — positions bypass the simulation's speed handling.

`age` holds the particle's lifetime in seconds, and `uv` its current screen-space position (useful for sampling the **Image** input as a screen-aligned map). Note the sampler names here: `ClampedSampler` and `WrappedSampler`.

Pushing particles out of a connected SDF field:

```hlsl
float d = GetDistance(pos);
float3 n = GetNormal(pos);

if (d < 0)
    vel += n / (d + 1);
```

The full camera and object transform matrices (`WorldToClipSpace`, `ObjectToWorld`, …) are available for advanced effects.

## CustomVertexShader

Runs once per vertex of the connected mesh. Modify the `PbrVertex v` — typically `v.Position`, `v.Normal` or `v.ColorRGB`.

Random displacement along the normals (the **SimpleNoise** preset):

```hlsl
float4 noise = hash41u(vertexIndex);
v.Position += v.Normal * (noise.xyz - 0.5) * Offset * A;
```

Coloring by a connected distance field:

```hlsl
// A: Falloff   B: Size
float d = GetField(float4(v.Position, 0)).w;
float t = Biased(smoothstep(1, 0, (d - A) / B));
v.ColorRGB = SampleGradient(t).rgb;
```

## CustomFaceShader

Runs once per triangle and writes the three vertices back **unshared** — every face gets its own vertices. That allows faceting effects that would be impossible with shared vertices: shrinking, rotating or shifting whole triangles. `pos1`–`pos3` keep the original positions while you modify `v1`–`v3`.

Coloring faces by their average height:

```hlsl
float3 avgPos = (v1.Position + v2.Position + v3.Position) / 3;
float4 color = SampleGradient((avgPos.y + Offset.y) * A + B);

v1.ColorRGB = color.rgb;
v2.ColorRGB = color.rgb;
v3.ColorRGB = color.rgb;
```

Exploding faces with per-face noise:

```hlsl
v1.Position += (hash41u(faceIndices.x).xyz - 0.5) * C;
v2.Position += (hash41u(faceIndices.y).xyz - 0.5) * C;
v3.Position += (hash41u(faceIndices.z).xyz - 0.5) * C;
```

## CustomSDF

Unlike the operators above, [CustomSDF] does not run as its own shader — it defines a node in TiXL's **shader graph**. You write the body of a distance function and the field pipeline assembles it into whatever shader samples it (e.g. [RaymarchField], or the Field input of the other Custom ops):

```hlsl
float dCustom(float3 p, float3 Offset, float A, float B, float C)
{
    // ← your DistanceFunction code; must return the signed distance at p
}
```

The default is a simple sphere:

```hlsl
return length(p - Offset) - A;
```

Organic blobs (from the **SinBlobs** preset):

```hlsl
float scale = A;
float thickness = B;
float bias = C;

p *= scale;
return (abs(dot(sin(p*.5 + Offset), cos(p.zxy * 1.23)) - bias) / scale - thickness) * 0.55;
```

A GLSL-style `mod(x, y)` macro is predefined, which makes porting fractal one-liners from Shadertoy or [jbaker.graphics](https://jbaker.graphics/writings/DEC.html) straightforward — many of the shipped presets (BoxFold, SpiderCave, PipeMaze, …) come from there. Put helper functions into **AdditionalDefines**. There is no Gradient or Image here — a distance function only returns geometry; color it with the field operators that consume it.

Visualize the result with [VisualizeFieldDistance], render it with [RaymarchField], and modify it with [BendField], [TransformField], [PolarRepeat] and friends.

## Using Fields

[CustomPointShader], [CustomForce], [CustomVertexShader] and [CustomFaceShader] have a **Field** input that accepts field operators — anything from the `field` namespace, including your own [CustomSDF]. The template exposes them through:

| Function                          | Description                                                            |
| --------------------------------- | ---------------------------------------------------------------------- |
| `GetField(float4 p)`              | Samples the connected field at `p.xyz`; returns rgb = color, w = distance |
| `GetDistance(float3 p)`           | Shorthand for the distance value                                       |
| `GetFieldNormal(float3 p)`        | Normalized field gradient (in [CustomForce] this is called `GetNormal`) |

This makes it easy to color, displace or collide against any SDF you can build in the graph.

## Built-in helper functions

Available in all template-based operators (everything except [CustomSDF], which only sees what its shader-graph context and **AdditionalDefines** provide):

| Name                            | Description                                            |
| ------------------------------- | ------------------------------------------------------ |
| `Biased(float f)`               | Applies the **GainAndBias** parameter remapping to f   |
| `SampleGradient(float f)`       | Samples the **Gradient** parameter at f (0…1)          |
| `ApplyGainAndBias(f, gainBias)` | Like `Biased` but with an explicit gain/bias `float2`  |
| `hash11(f)` … `hash44(v)`       | Fast hash functions: digits = output/input component count, e.g. `hash41u(uint)` returns a `float4` from a `uint` seed |

Noise functions (`cnoise`, `snoise`, `curlNoise`, …) are included in [CustomVertexShader] and [CustomFaceShader]. For the other ops you can include them via `AdditionalCode`.

### Using Quaternions

Point rotations are stored as quaternions. These helpers are available in all ops except [CustomPixelShader]:

| Name | Description | Parameters |
| --- | --- | --- |
| `qMul` | Performs standard Hamiltonian product of two quaternions. | `float4 q1`, `float4 q2` |
| `qRotateVec3` | Rotates a 3D vector by a quaternion using an optimized formula. | `float3 v`, `float4 q` |
| `qConjugate` | Returns the conjugate of a quaternion (negates the imaginary part). | `float4 q` |
| `qInverse` | Calculates the inverse of a quaternion. | `float4 q` |
| `qFromAngleAxis` | Creates a quaternion representing a rotation of `angle` around `axis`. | `float angle`, `float3 axis` |
| `qFromVectors` | Creates a quaternion representing the rotation from `v1` to `v2`. | `float3 v1`, `float3 v2` |
| `qLookAt` | Creates a rotation quaternion that points toward `forward` with specialized `up`. | `float3 forward`, `float3 up` |
| `qSlerp` | Performs Spherical Linear Interpolation between two quaternions. | `float4 a`, `float4 b`, `float t` |
| `qFromEuler` | Converts Euler angles (yaw, pitch, roll) to a quaternion. | `float yaw`, `float pitch`, `float roll` |
| `qToMatrix` | Converts a quaternion into a $4\times4$ rotation matrix. | `float4 quat` |
| `qFromMatrix3` | Simplified conversion from a $3\times3$ matrix to a quaternion. | `float3x3 m` |
| `qFromMatrix3Precise` | Robust conversion from a $3\times3$ matrix to a quaternion (handles all cases). | `float3x3 m` |

Example — orienting points along a generated curve (from the **Knot** preset, with the `Generator` function defined in `AdditionalDefines`):

```hlsl
p.Position = Generator(f);
float3 up = float3(0,-1,0);
float3 fwd = normalize(Generator(f - .01) - Generator(f + .01));
p.Rotation = qLookAt(fwd, up);
```

## Tips

- **Open the template.** The `TemplateFile` parameter points to the full shader source — it is the authoritative reference for what surrounds your code and which registers are bound.
- **Use the presets as starting points.** They are regular parameter snapshots; saving your own preset captures the shader code along with all parameters.
- **Comment your parameters.** A line like `// A: Falloff` at the top of the fragment makes presets reusable for others.
- **Compile errors** appear on the operator — click it to read the HLSL compiler message; line numbers refer to the assembled shader, so check the template for context.

## See also

- [Writing code operators](WritingCodeOps.md) — when a shader fragment isn't enough
- [Shader development example](ShaderDevelopmentExample.md) — working on full shader files with hot reload
