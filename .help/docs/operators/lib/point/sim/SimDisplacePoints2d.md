# SimDisplacePoints2d

*in [Lib.point.sim](README.md)*

Applies a displacement to the current point position.
This operator modifies the incoming buffer and thus can be used for simulations with [KeepPoints].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **DisplaceAmount** (Single) | — |
| **DisplaceOffset** (Single) | — |
| **Twist** (Single) | — |
| **Texture** (Texture2D Required) | — |
| **Center** (Vector3) | — |
| **TextureScale** (Vector2) | — |
| **TextureRotate** (Vector3) | — |
| **TextureMode** (TextureAddressMode) | — |
| **SampleRadius** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **OutBuffer** | T3.Core.DataTypes.BufferWithViews |

