# RadialGradient

*in [Lib.image.generate.basic](README.md)*

Generates an image with a radial gradient.


Similar Ops: [LinearGradient] [NGon] [RoundedRect] [Rings]  [Blob]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Gradient** (Gradient) | — |
| **Width** (Single) | The size of the gradient.<br/>Flips the gradient if smaller than 0. |
| **Stretch** (Vector2) | An additional stretch factor<br/> |
| **Offset** (Single) | Offsets the gradient.  |
| **PingPong** (Boolean) | Repeats the gradient once.<br/>This can be useful for rings. |
| **Repeat** (Boolean) | Repeats the gradient. |
| **Center** (Vector2) | Center of the gradient |
| **PolarOrientation** (Boolean) | If enabled switches to a "star like" polar coordiante orienation. |
| **BiasAndGain** (Vector2) | Applies Gain and Bias before sampling the gradiant. |
| **Noise** (Single) | Adding slight noise can improve color banding. |
| **BlendMode** (Int32) | Blendmode when applied onto a connected input image. |
| **Resolution** (Int2) | The resolution. <br/><br/>If 0,0 will use requested resuition of the input resolution if connected. |
| **TextureFormat** (Format) | — |
| **GenerateMipMaps** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

