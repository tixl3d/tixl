# MotionBlur

*in [Lib.render.postfx](README.md)*

Blurs moving parts of a rendered image along their motion, like a camera with an open shutter.

Each pixel's motion comes from the velocity buffer that [RenderScene] renders. Where nothing wrote velocity, the camera motion is reconstructed from the depth buffer, which only blurs static geometry. Fast objects smear over the background and the background shows through their edges.

Render the scene with [RenderScene] and connect only Image. With a plain [RenderTarget], connect DepthMap, VelocityMap and the scene camera.

Place it after [TXAA] or [ScreenSpaceLighting], whose temporal history needs the unblurred image.

Only what is visible on screen can smear, and rotations blur along straight lines. For exports use [RepeatFrame], which renders the scene several times per frame.

Also see [DirectionalBlur], [DepthOfField]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D Required) | The rendered image. An image from [RenderScene] brings depth, velocity and camera along. |
| **DepthMap** (Texture2D) | Overrides the depth buffer from the image's bundle. |
| **VelocityMap** (Texture2D) | Overrides the velocity buffer from the image's bundle. Pixels without velocity use the camera motion. |
| **CameraReference** (Object) | Overrides the camera from the image's bundle. Needed for the camera motion where no velocity was written. |
| **Shutter** (Single) | Share of the frame the shutter is open: 0.5 is a 180° shutter as in film, 1 blurs over a whole frame of motion. Higher values exaggerate. The length stays steady when frame times vary. |
| **MaxBlur** (Single) | Longest blur in each direction, in pixels. Larger values cost more, because the tiles grow with it. |
| **SampleCount** (Int32) | Samples along the motion. More samples reduce the grain in long blurs. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

## How it works

The blur is reconstructed in four passes, after McGuire et al., "A Reconstruction Filter for Plausible Motion Blur" (2012), with the jittered sampling from Jimenez, "Next Generation Post Processing in Call of Duty: Advanced Warfare" (2014):

1. Each pixel's blur radius in pixels, from velocity or the camera motion, scaled by Shutter and limited by MaxBlur.
2. The largest radius per tile. Tiles are at least 16 pixels and at least MaxBlur wide.
3. The largest radius among each tile and its eight neighbors: every pixel whose blur can reach this one lies in that area.
4. Each pixel samples along that dominant motion. Samples in front count where their own blur reaches the pixel; samples behind show through where the pixel's blur makes it transparent. Without motion nearby, the pass returns the pixel unchanged.
