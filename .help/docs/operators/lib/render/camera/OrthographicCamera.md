# OrthographicCamera

*in [Lib.render.camera](README.md)*

Set an orthographic project matrix for the operators following further down (i.e., to the left).

Note that this projection uses a special z-buffer depth that might be incompatible with other operators like [DepthBufferAsGrayScale]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | — |
| **Position** (Vector3) | — |
| **Target** (Vector3) | — |
| **Up** (Vector3) | — |
| **Roll** (Single) | — |
| **NearFarClip** (Vector2) | — |
| **Scale** (Single) | — |
| **AspectRatio** (Single) | — |
| **Stretch** (Vector2) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **Reference** | System.Object |

