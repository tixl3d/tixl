# Raster3dField

*in [Lib.field.generate.texture](README.md)*

Uses TiXL's Raymarching integration to generate a procedural raster 3D Texture that can be mapped onto meshes via [DrawMesh].
Connects into the 'FragmentField' input of [DrawMesh].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ColorA** (Vector4) | — |
| **ColorB** (Vector4) | — |
| **Offset** (Vector3) | — |
| **Scale** (Vector3) | — |
| **LineWidth** (Single) | — |
| **Feather** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.ShaderGraphNode |

