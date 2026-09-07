# TextureMapForce

*in [Lib.particle.force](README.md)*

Accelerate particles from a signed normal map. The map is stretched to the camera clip space.

This force can be very powerful in creating effects, such as particles flowing along images or tracing contours. It utilizes the R and G channels of the signed normal map to accelerate along the X and Y axes.

Important: This implies that the normal map needs to have negative values. One option is using [NormalMap] with the Gray_ToNormalizedRGBSigned mode. This can be especially potent when combined with the Rotation parameter.

Tip: You can also use the [ImageLevels] operator to verify that the texture contains negative values.

You can use the ViewConfinement and DepthConcentration parameters to keep the particles within the visible area for the camera view.

Please also check the example.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SignedNormalMap** (Texture2D Required) | — |
| **Amount** (Single) | How much are the particles accelerated by the signed normal map.<br/>You can use negative values to invert the direction. |
| **AmountXY** (Vector2) | — |
| **AmountVariation** (Single) | Add random variation to the amount of the effect. <br/>You can use the BiasAndGain parameter to control the variation distribution. |
| **Twist** (Single) | — |
| **TwistVariation** (Single) | — |
| **Spin** (Single) | Applies a rotation along the view direction. |
| **SpinVariation** (Single) | Add a random offset to the twist rotation. Use BiasAndGain to control the amount of random distribution |
| **VariationGainAndBias** (Vector2) | Controls the random distribution. Please check [BiasAndGainExample] and read the documentation on Wiki -> Rendering for Artists. |
| **ViewConfinement** (Single) | Applies a force to push particles back into view. |
| **DepthConcentration** (Single) | Applies force to push particles towards distance from the camera that is defined by CenterDepth. |
| **CenterDepth** (Single) | Target distance from the camera plane when applying DepthConcentration. |
| **ShowGizmo** (GizmoVisibility) | — |
| **Colorization** (Vector4) | — |
| **Colorize** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Particles** | T3.Core.DataTypes.ParticleSystem |

