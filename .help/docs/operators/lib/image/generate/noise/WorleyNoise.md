# WorleyNoise

*in [Lib.image.generate.noise](README.md)*

Also called Voronoi noise and cellular noise.
It allows creating nice textures, from organic to science fiction spaceship hull. 

It has various methods like:
* Manhattan Worley
* Chebyshev Worley

Combine it with [Steps] or / and [DetectEdges] for example. 

Available Noise Ops [WorleyNoise] [FractalNoise] [ShardNoise]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D) | — |
| **TextureBlend** (Single) | — |
| **ColorA** (Vector4) | — |
| **ColorB** (Vector4) | — |
| **Scale** (Single) | — |
| **Stretch** (Vector2) | — |
| **Offset** (Vector2) | — |
| **Phase** (Single) | — |
| **Randomness** (Single) | — |
| **Clamping** (Vector2) | — |
| **GainAndBias** (Vector2) | — |
| **Method** (Int32) | — |
| **Resolution** (Int2) | — |
| **GenerateMips** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

