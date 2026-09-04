# ScreenCloseUp

*in [Lib.image.fx.stylize](README.md)*

Uses the incoming 2D image and puts it on a virtual LCD/screen in a 3D scene that looks as if it is being filmed by a real camera (including depth of field etc)

Works great with [PlayVideo] or any [RenderTarget] or [ScreenCapture].

Additional ops can be used to emphasize the illusion of a real camera such as: [ChromaticAbberation], [ColorGrade], [Grain]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **Zoom** (Single) | Defines the distance to the virtual LCD/Screen |
| **Target** (Vector2) | Offsets the target of the virtual camera |
| **Tilt** (Vector2) | Tilts/offsets the virtual camera without moving the target <br/>X = Left / Right<br/>Y = Up / Down |
| **FogStrength** (Single) | Defines the intensity of the fog (looks like a vignette) |
| **Glossy** (Single) | Defines the glossiness of the screen |
| **DepthOfField** (Single) | Defines how large the depth of field range is |
| **BackgroundColor** (Vector4) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

