# GetPosition

*in [Lib.flow.context](README.md)*

Gets the current position, rotation, and scale in the world so it can be applied to other transforms.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Space** (Int32) | — |
| **PositionOffset** (Vector3) | — |

## Outputs
| Name | Type |
|---|---|
| **UpdateCommand** | T3.Core.DataTypes.Command |
| **Position** | System.Numerics.Vector3 |
| **Scale** | System.Numerics.Vector3 |
| **ObjectToWorld** | System.Numerics.Vector4[] |

