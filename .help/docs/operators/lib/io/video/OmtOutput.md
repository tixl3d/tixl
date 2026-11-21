# OmtOutput

*in [Lib.io.video](README.md)*

Sends a texture as an OMT source that other OMT receivers (including [OmtInput](OmtInput.md) on
another machine) can discover and receive. Frames are compressed with the VMX codec and advertised
through DNS-SD.

The texture must be an 8-bit RGBA/BGRA format (e.g. `R8G8B8A8_UNorm`); use a [ConvertFormat] or a
[RenderTarget] to produce it. **SourceAddress** reports the address the sender advertises and is
empty until the first frame was sent. **EnableAlpha** sends BGRA frames instead of the packed UYVY
path; leave it off for opaque content.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Texture** (Texture2D) | The frame to send. 8-bit RGBA/BGRA formats are required. |
| **SenderName** (String) | The name the source advertises; receivers see it in their dropdown. |
| **FrameRate** (Int32) | Target send rate in frames per second. |
| **EnableAlpha** (Boolean) | Send BGRA frames with alpha instead of opaque UYVY frames. |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |
| **SourceAddress** | System.String |

**See also:** [OmtInput](OmtInput.md), [NdiOutput](NdiOutput.md), [SpoutOutput](SpoutOutput.md)

---
*Generated from operator definition in Lib.io.video*
