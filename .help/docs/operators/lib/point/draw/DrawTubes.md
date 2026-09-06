# DrawTubes

*in [Lib.point.draw](README.md)*

Draws a shaded 3D mesh for connected lines points.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | — |
| **Color** (Vector4) | — |
| **Width** (Single) | — |
| **Spin** (Single) | — |
| **Twist** (Single) | — |
| **TextureMode** (Int32) | — |
| **TextureRange** (Vector2) | — |
| **EnableDepthWrite** (Boolean) | — |
| **BlendMod** (Int32) | — |
| **Culling** (CullMode) | — |
| **WidthFactor** (Int32) | Selects the point attribute used as a per-point width weight. None = uniform width, F1/F2 = use the point's FX1/FX2 value (NaN falls back to 1). |
| **UseScale** (Boolean) | Scales the tube from the points' Scale attribute instead of a width weight. With Uniform Scale on, only Scale.x is used as a single factor. |
| **UniformScale** (Boolean) | On: uses Scale.x as a single uniform width factor.<br/>Off: uses all three axes independently - X stretches or compresses the line along its direction around each point (1 = no change), Y and Z scale the cross-section into an ellipse. |
| **Sides** (Int32) | Number of sides around the tube cross-section. |
| **CapStart** (Boolean) | Draws a flat cap closing the start of the tube. |
| **CapEnd** (Boolean) | Draws a flat cap closing the end of the tube. |
| **Smooth** (Boolean) | Interpolates the line with a smooth curve instead of straight segments. |
| **RoundAmount** (Single) | Bulges each segment's width in its middle. Positive = rounded joints, negative = pinched. |
| **SubSegments** (Int32) | Curve subdivisions per segment while Smooth is on. Higher = smoother curves. |
| **DistanceFade** (Boolean) | Fades the tube width with camera distance: full width up to Fade Start Dist, shrinking to Min Width Factor x Width at Fade End Dist. |
| **FadeStartDist** (Single) | Camera distance at which the width fade starts. The tube is at full width up to this distance. |
| **FadeEndDist** (Single) | Camera distance at which the width fade reaches its minimum (Min Width Factor x Width). |
| **MinWidthFactor** (Single) | Multiplier applied to Width at maximum fade distance. 1 = no fade, 0 = fully faded out. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
