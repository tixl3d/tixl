# ScreenCapture

*in [Lib.io.video](README.md)*

Loads and renders the content of the entire screen, similar to screen capturing software like OBS (Open Broadcaster Software).

Useful combinations: Can be rendered to a file/image sequence within TiXL's 'Render To File' window to make a recording of TiXL's UI.
Can be combined with [ScreenCloseUp] to make it look like the monitor is being filmed with a real camera.
Can also be used to add 2D effects on the captured image like [Pixelate], [SortPixel], etc.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ScreenIndex** (Int32) | Defines which screen is recorded (e.g., for multi-monitor systems) |
| **TimeOut** (Int32) | Defines the timeout |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |

