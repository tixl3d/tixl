# MapPointAttributes

*in [Lib.point.modify](README.md)*

Sets the points attribute and color from input attributes. This can be very powerful to remap point attributes.

Different modes allow you to...
- distribute the curve range along all points
- or use different mapping ranges.

You can also connect a texture to provide an override for the curve.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Points** (BufferWithViews Required) | — |
| **InputMode** (Int32) | Defines how the normalized input value is computed for each point.<br/><br/>- The default will use 0 for the first point and 1 for the last, interpolating between.<br/>- F1 and F2 will use the point attributes. This can be very powerful, e.g., remap the age of particles to a color or size.<br/>- Random will assign a random value between 0 and 1 for each point.<br/> |
| **Strength** (Single) | Defines how the effect is applied, with 0 effectively bypassing the effect.<br/><br/>Note the values below 0 or above 1 are allowed but might yield unpredictable effects. |
| **StrengthFactor** (Int32) | — |
| **Mapping** (Int32) | Allows the initial input value to be remapped. <br/>Combining this with Range and Phase can be powerful to build transitions for animated effects. |
| **Range** (Single) | Can rescale the input range. Smaller value will effectively narrow the range or increase the number of repetitions respectively. |
| **Phase** (Single) | Allows the range to be shifted. This can be useful to build transitions or cycle through repetitive configurations. |
| **WriteColor** (Int32) | Defines how the gradient colors are applied. |
| **Gradient** (Gradient) | Defines the colors to be applied for remapped input range.<br/><br/>Tip: Try to use a HOLD interpolated gradient with Random input mode. |
| **WriteTo** (Int32) | Defines to which attribute the result should be written. |
| **WriteMode** (Int32) | Allow different configurations of how the new values should be applied. |
| **MappingCurve** (Curve) | Defines the actual remapping curve. <br/>Note that only the x-range between 0 and 1 is used. |
| **ValueTexture** (Texture2D) | Allows overriding the curve with a texture. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.BufferWithViews |

