# CustomVertexShader

*in [Lib.mesh.modify](README.md)*

Uses custom shader code to manipulate the vertices of a mesh.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Mesh** (MeshBuffers) | — |
| **SplitVertices** (Boolean) | — |
| **RecomputeNormals** (Boolean) | — |
| **Offset** (Vector3) | — |
| **A** (Single) | — |
| **B** (Single) | — |
| **C** (Single) | — |
| **D** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **Image** (Texture2D) | — |
| **Gradient** (Gradient) | — |
| **ShaderCode** (String) | — |
| **AdditionalDefines** (String) | — |
| **ShaderTemplate** (String) | — |
| **ShaderGraph** (ShaderGraphNode) | — |

## Outputs
| Name | Type |
|---|---|
| **MeshBuffers** | T3.Core.DataTypes.MeshBuffers |
| **GeneratedCode** | System.String |

