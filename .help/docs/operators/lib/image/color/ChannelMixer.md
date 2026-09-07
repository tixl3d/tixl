# ChannelMixer

*in [Lib.image.color](README.md)*

Adjusts the color of an incoming image.

Each column defines a color channel of the output, whereas the incoming components are multiplied by a factor and summed up.
Finally, a value is added to each component.

You can do a lot of things with this operator, like:
- converting to grayscale
- extracting an alpha channel to grayscale
- inverting an image
- swapping color channels.

Check the presets for examples.
For more natural color correction, try [Adjust Color].

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture2d** (Texture2D Required) | — |
| **MultiplyR** (Vector4) | — |
| **MultiplyG** (Vector4) | — |
| **MultiplyB** (Vector4) | — |
| **MultiplyA** (Vector4) | — |
| **Add** (Vector4) | — |
| **GenerateMipmaps** (Boolean) | — |
| **ClampResult** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

