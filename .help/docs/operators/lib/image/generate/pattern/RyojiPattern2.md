# RyojiPattern2

*in [Lib.image.generate.pattern](README.md)*

A pattern generator inspired by the work of Ryoji Ikeda. It subdivides the image space recursively and can generate a range of different patterns.

For an earlier version with fewer options see: [RyojiPattern1]

Other interesting patterns can be generated with [SinForm] [ZollnerPattern] [FraserGrid] [Raster] [CheckerBoard]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Background** (Vector4) | — |
| **Foreground** (Vector4) | — |
| **MixOriginal** (Single) | — |
| **Contrast** (Single) | — |
| **ForgroundRatio** (Single) | — |
| **Highlight** (Vector4) | — |
| **HighlightProbability** (Single) | — |
| **HighlightSeed** (Int32) | — |
| **Splits** (Vector2) | — |
| **SplitB** (Vector2) | — |
| **SplitC** (Vector2) | — |
| **SplitProbability** (Vector2) | — |
| **ScrollSpeed** (Vector2) | — |
| **ScrollProbability** (Vector2) | — |
| **ScrollOffset** (Single) | — |
| **Padding** (Vector2) | — |
| **Seed** (Single) | — |
| **Resolution** (Int2) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

