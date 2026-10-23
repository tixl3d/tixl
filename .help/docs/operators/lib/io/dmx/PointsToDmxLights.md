# PointsToDmxLights

*in [Lib.io.dmx](README.md)*

Converts 3D points into DMX channel values for lighting fixtures. Supports position, rotation (pan/tilt), color and custom features, with universe packing options for multi-universe rigs.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **EffectedPoints** (BufferWithViews) | Buffer of points to convert into DMX channel values. Each point carries position, orientation, color and F1/F2 feature values for its fixture (or pixel). |
| **ReferencePoints** (BufferWithViews) | Optional buffer with one reference point per fixture (e.g. its mounting position). EffectedPoints are grouped per reference point and positions/rotations are measured relative to it (look-at). Requires the EffectedPoints count to be a multiple of the reference count, otherwise falls back to 1-to-1 mapping. |
| **FixtureChannelSize** (Int32) | Number of DMX channels each fixture occupies. All channel numbers below are 1-based within this fixture block. Set to 0 to disable DMX generation. |
| **FitInUniverse** (Boolean) | Prevent a fixture's channel block from crossing a 512-channel universe boundary by padding with empty channels, so every fixture fits completely into a single universe. |
| **FillUniverse** (Boolean) | Pad each universe with zero channels up to the full 512 slots, for interfaces that expect complete universes. |
| **FixturesPerUniverse** (Int32) | Maximum number of fixtures placed in each 512-channel universe before the next fixture starts at the next universe boundary. 0 disables the limit and packs fixtures by channel count only. |
| **DebugToLog** (Boolean) | Log detailed debug output (pan/tilt angles, DMX mappings, custom variables) for the first fixture to the console. |
| **TestModeSelect** (Int32) | Debug helper that overrides the rotation forward vector with a fixed axis direction to verify the pan/tilt channel mapping without live input data. |
| **GetPosition** (Boolean) | Enable deriving a distance from the point position and writing it to a DMX channel. |
| **PositionMeasureAxis** (Int32) | World axis along which the position distance is measured (X, Y or Z). Disabled turns position measurement off. |
| **InvertPositionDirection** (Boolean) | Negate the measured distance before mapping it onto the DMX range. |
| **PositionDistanceRange** (Vector2) | Minimum and maximum distance (world units) mapped onto the 8- or 16-bit DMX range. Distances outside are clamped; min and max must differ. |
| **PositionChannel** (Int32) | 1-based coarse DMX channel within the fixture block receiving the position value. 0 disables position output. |
| **PositionFineChannel** (Int32) | Optional fine channel for 16-bit position output; coarse and fine combine into a 16-bit value. |
| **GetRotation** (Boolean) | Enable pan/tilt calculation from the point orientation (or the look-at direction when ReferencePoints are connected) and write it to DMX. |
| **ForwardVector** (Int32) | The fixture's local forward axis (beam direction at neutral orientation) used to extract the aiming direction from the rotation. |
| **RotationOrder** (Int32) | Order in which pan and tilt rotations combine: PanThenTilt or TiltThenPan. Must match the fixture's mechanical rotation order. |
| **InvertX** (Boolean) | Invert the X component of the extracted aiming direction. |
| **InvertY** (Boolean) | Invert the Y component of the extracted aiming direction. |
| **InvertZ** (Boolean) | Invert the Z component of the extracted aiming direction. |
| **PanAxis** (Int32) | The fixture's mechanical pan (horizontal) axis in its local space. |
| **InvertPan** (Boolean) | Mirror the pan angle within its range before writing it to DMX. |
| **TiltAxis** (Int32) | The fixture's mechanical tilt (vertical) axis in its local space. |
| **InvertTilt** (Boolean) | Mirror the tilt angle within its range before writing it to DMX. |
| **ShortestPathPanTilt** (Boolean) | Track the previous pan/tilt per fixture and pick the equivalent angle - including the flipped solution (pan+180 degrees, 180 degrees minus tilt) - closest to the last value, avoiding full-range swings on wrap-around. |
| **PanRange** (Vector2) | Minimum and maximum pan angle in degrees supported by the fixture (e.g. 0 to 540). Computed angles are shifted and clamped into this range. |
| **PanChannel** (Int32) | 1-based coarse DMX channel within the fixture block for pan. 0 disables pan output. |
| **PanFineChannel** (Int32) | Optional fine channel for 16-bit pan output. |
| **TiltRange** (Vector2) | Minimum and maximum tilt angle in degrees supported by the fixture (e.g. -90 to 90). Computed angles are shifted and clamped into this range. |
| **TiltChannel** (Int32) | 1-based coarse DMX channel within the fixture block for tilt. 0 disables tilt output. |
| **TiltFineChannel** (Int32) | Optional fine channel for 16-bit tilt output. |
| **PanOffset** (Single) | Constant angle in degrees added to the computed pan before range mapping. A value of 90 means no offset. |
| **TiltOffset** (Single) | Constant angle in degrees added to the computed tilt before range mapping. A value of 90 means no offset. |
| **VisPanAxis** (Int32) | Pan axis used when reassembling orientations for the VisualizeLights output. Falls back to PanAxis when Disabled. |
| **VisTiltAxis** (Int32) | Tilt axis used when reassembling orientations for the VisualizeLights output. Falls back to TiltAxis when Disabled. |
| **GetColor** (Boolean) | Enable writing the point colors to DMX channels. |
| **RgbToCmy** (Boolean) | Invert the color components for CMY fixtures (C=1-R, M=1-G, Y=1-B). White derives from the converted values. |
| **RedChannel** (Int32) | 1-based DMX channel for the red component within the fixture (or per-pixel) color block. 0 disables red. |
| **GreenChannel** (Int32) | 1-based DMX channel for the green component within the fixture (or per-pixel) color block. 0 disables green. |
| **BlueChannel** (Int32) | 1-based DMX channel for the blue component within the fixture (or per-pixel) color block. 0 disables blue. |
| **AlphaChannel** (Int32) | Optional 1-based DMX channel for the point's alpha (W) component. 0 disables. |
| **WhiteChannel** (Int32) | Optional 1-based DMX channel for white, derived as the minimum of red, green and blue. 0 disables. |
| **Is16BitColor** (Boolean) | Write each color component as a 16-bit coarse/fine channel pair, doubling the channels per component. |
| **GetF1** (Boolean) | Enable writing the first feature value (F1) of the points to DMX. |
| **GetF1ByPixel** (Boolean) | Write F1 once per pixel (channel advances per pixel) instead of once per fixture. |
| **F1Channel** (Int32) | 1-based DMX channel within the fixture block for F1. 0 disables F1 output. |
| **GetF2** (Boolean) | Enable writing the second feature value (F2) of the points to DMX. |
| **GetF2ByPixel** (Boolean) | Write F2 once per pixel (channel advances per pixel) instead of once per fixture. |
| **F2Channel** (Int32) | 1-based DMX channel within the fixture block for F2. 0 disables F2 output. |
| **CustomVariableChannels** (List`1) | List of extra 1-based DMX channel numbers within the fixture block that receive fixed values (e.g. dimmer, strobe, zoom). |
| **CustomVariableValues** (List`1) | Raw DMX values (0-255) written to CustomVariableChannels. The list is padded with 128 or trimmed to match the channel list length. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Collections.Generic.List`1[System.Int32] |
| **VisualizeLights** | T3.Core.DataTypes.BufferWithViews |
