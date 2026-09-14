# DrawTubes

*in [Lib.point.draw](README.md)*

Draws a shaded 3D mesh for connected lines points.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **GPoints** (BufferWithViews Required) | The point buffer to draw. Points are connected in order into a tube. |
| **Color** (Vector4) | Base tint multiplied against each point's color and the shading result. |
| **Width** (Single) | Base radius of the tube in world units. Per-point Width Factor / Scale multiplies this value. |
| **Spin** (Single) | Constant rotation of the cross-section around the line direction, in degrees. |
| **Twist** (Single) | Extra rotation that accumulates along the tube length (degrees) — spirals the cross-section from start to end. |
| **UseWAsU** (Boolean) | Drive the U texture coordinate from each point's FX1 value (interpolated along the tube) instead of arc length — useful for animating or slicing a texture with per-point data. Off = physical arc-length UVs at a constant texel size. |
| **TextureRange** (Vector2) | Affine remap applied to the procedural U coordinate (default 0..1 = identity). UVs are generated at a constant texel size set by Texture Scale: the side uses cylindrical projection (U along arc length, V around the circumference) and end caps use a planar disk placed adjacent to each tube end. |
| **EnableDepthWrite** (Boolean) | Whether the tube writes to the depth buffer. Disable for additive/transparent tubes that should not occlude. |
| **BlendMod** (Int32) | Blending mode used when compositing the tube against what is behind it. |
| **Culling** (CullMode) | Face culling mode (None / Front / Back). Default Back culls back-facing triangles for a solid tube. |
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
| **TextureScale** (Single) | World units per one texture repeat. Sets the texel size for all generated UVs (side and caps) so the pattern stays a constant size along and around the tube. Smaller = denser texture. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
