# PointsToDmxLights

*in [Lib.io.dmx](README.md)*

Converts 3D points into DMX channel values for lighting fixtures. Supports position, rotation (pan/tilt), color, and custom features.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **EffectedPoints** (BufferWithViews) | — |
| **ReferencePoints** (BufferWithViews) | — |
| **FixtureChannelSize** (Int32) | — |
| **FitInUniverse** (Boolean) | — |
| **FillUniverse** (Boolean) | — |
| **DebugToLog** (Boolean) | — |
| **TestModeSelect** (Int32) | — |
| **GetPosition** (Boolean) | — |
| **PositionMeasureAxis** (Int32) | — |
| **InvertPositionDirection** (Boolean) | — |
| **PositionDistanceRange** (Vector2) | — |
| **PositionChannel** (Int32) | — |
| **PositionFineChannel** (Int32) | — |
| **GetRotation** (Boolean) | — |
| **ForwardVector** (Int32) | — |
| **RotationOrder** (Int32) | — |
| **InvertX** (Boolean) | — |
| **InvertY** (Boolean) | — |
| **InvertZ** (Boolean) | — |
| **PanAxis** (Int32) | — |
| **InvertPan** (Boolean) | — |
| **TiltAxis** (Int32) | — |
| **InvertTilt** (Boolean) | — |
| **ShortestPathPanTilt** (Boolean) | — |
| **PanRange** (Vector2) | — |
| **PanChannel** (Int32) | — |
| **PanFineChannel** (Int32) | — |
| **TiltRange** (Vector2) | — |
| **TiltChannel** (Int32) | — |
| **TiltFineChannel** (Int32) | — |
| **PanOffset** (Single) | — |
| **TiltOffset** (Single) | — |
| **VisPanAxis** (Int32) | — |
| **VisTiltAxis** (Int32) | — |
| **GetColor** (Boolean) | — |
| **RgbToCmy** (Boolean) | — |
| **RedChannel** (Int32) | — |
| **GreenChannel** (Int32) | — |
| **BlueChannel** (Int32) | — |
| **AlphaChannel** (Int32) | — |
| **WhiteChannel** (Int32) | — |
| **Is16BitColor** (Boolean) | — |
| **GetF1** (Boolean) | — |
| **GetF1ByPixel** (Boolean) | — |
| **F1Channel** (Int32) | — |
| **GetF2** (Boolean) | — |
| **GetF2ByPixel** (Boolean) | — |
| **F2Channel** (Int32) | — |
| **CustomVariableChannels** (List`1) | — |
| **CustomVariableValues** (List`1) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Collections.Generic.List`1[System.Int32] |
| **VisualizeLights** | T3.Core.DataTypes.BufferWithViews |

