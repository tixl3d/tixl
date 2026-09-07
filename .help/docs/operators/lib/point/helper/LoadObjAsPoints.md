# LoadObjAsPoints

*in [Lib.point.helper](README.md)*

Loads an OBJ point cloud file. These files can be generated with Blender or MeshLab and can contain color information.

You can use selected operators like [DrawBillboards] that can use the point rotation attribute (XYZW) as RGBA. This is a hack.

You can also use the [LineVertices] mode to import OBJ files with line definitions and draw them as lines like so:
[LoadObjAsPoints]->[PrepareSvgLineTransition]->[ListToBuffer]->[DrawLinesBuildup]


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Path** (String) | — |
| **Mode** (Int32) | — |
| **Sorting** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.StructuredList |

