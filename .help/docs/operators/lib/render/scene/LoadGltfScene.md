# LoadGltfScene

*in [Lib.render.scene](README.md)*

Experimental op for loading complex scene setups using the glTF Format. Scenes have to be connected to a [DrawScene]-Op to be rendered.
This Operator supports *.glb and *.gltf + .bin + textures.







Also see [HowToDrawThings] 

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Path** (String) | — |
| **Setup** (SceneSetup) | — |
| **CombineBuffer** (Boolean) | — |
| **TriggerUpdate** (Boolean) | — |
| **OffsetRoughness** (Single) | This is a temporary offset that probably will become obsolete soon. |
| **OffsetMetallic** (Single) | This is a temporary offset that probably will become obsolete soon. |
| **MeshChildIndex** (Int32) | Defines which visible child will be used for Mesh and Material output. |

## Outputs
| Name | Type |
|---|---|
| **ResultSetup** | System.Single |
| **Mesh** | T3.Core.DataTypes.MeshBuffers |
| **Material** | T3.Core.Rendering.Material.PbrMaterial |

