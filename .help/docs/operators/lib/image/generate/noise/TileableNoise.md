# TileableNoise

*in [Lib.image.generate.noise](README.md)*

Generates a procedural fractal noise image effect also known as pink noise or fractional noise that can be used to create visual patterns that look like cloud, smoke, dust and scratches on film and similar

Available Noise Ops [WorleyNoise] [FractalNoise] [ShardNoise]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ColorA** (Vector4) | — |
| **ColorB** (Vector4) | — |
| **Detail** (Int32) | The base period of the repeated noise |
| **Octaves** (Int32) | The complexity of the perline noise.<br/>I.e. the number of iterations the noise is computed. |
| **Gain** (Single) | The gain factor for each iteration step. |
| **Lacunarity** (Single) | The scale factor for each iteration step. |
| **RandomPhase** (Single) | The 3rd coordinate of the 3d noise. Can be usedf for animations. |
| **Offset** (Vector2) | — |
| **Contrast** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **Scale** (Single) | An additional scale factor that WILL ADD SEAMS. |
| **Resolution** (Int2) | — |
| **GenerateMips** (Boolean) | — |
| **OutputFormat** (Format) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

