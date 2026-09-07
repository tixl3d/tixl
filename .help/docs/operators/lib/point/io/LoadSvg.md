# LoadSvg

*in [Lib.point.io](README.md)*

Loads an SVG file as points so it can be rendered as points or lines. The supported SVG feature set is very basic, and depending on your exporting applications, elements could be missing or not correctly represented. Missing SVG features include: - Masking - Text - Fill - Effects - Stroke styles like dashes The "ImportAs Lines" mode inserts double points so closed shapes will be closed when rendering lines. To draw the SVG, you can convert the list to a GPU point buffer with [ListToBuffer] and then use [DrawLines]. Also see: [PrepareSvgLineTransition], [SvgLineTransitionExample]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **FilePath** (String) | — |
| **Scale** (Single) | — |
| **CenterToBounds** (Boolean) | — |
| **ScaleToBounds** (Boolean) | — |
| **ImportAs** (Int32) | — |
| **ReduceFactor** (Single) | — |
| **SelectSingleShape** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **ResultList** | T3.Core.DataTypes.StructuredList |

