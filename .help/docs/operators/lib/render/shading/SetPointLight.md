# SetPointLight

*in [Lib.render.shading](README.md)*

Adds a point light into the scene which illuminates geometry using a combination of "Color", "Intensity" and "Decay".

Beware that [SetEnvironment] also affects the lighting in your scene, even if you are not using it, because every scene is always lit by a Default CubeMap (that can be deactivated by manually adding a [SetEnvironment]).

Currently a maximum of 8 Pointlights can be used in your scene. For example by daisy-chaining them right before your graph goes into the camera.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command) | — |
| **Position** (Vector3) | Changes the position of the PointLight:<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards) |
| **Intensity** (Single) | Increases or decreases the intensity of the light in relation to its decay.<br/> |
| **Color** (Vector4) | Sets the color of the light. |
| **Range** (Single) | This setting is currently ignored. It is recommended to use the "Decay" setting instead.<br/> |
| **Decay** (Single) | Controls how far the light shines into the scene.<br/><br/>A low decay setting means the light is shining far.<br/>A high decay setting means the light reaches a short distance. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

