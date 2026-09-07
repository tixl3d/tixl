# PointLight

*in [Lib.render.shading](README.md)*

The standard point light with transform gizmo.
This can bring light into your scene using a combination of "Color", "Intensity" and "Decay".
Beware that [SetEnvironment] also affects the lighting in your scene, even if you are not using it, because every scene is always lit by a Default CubeMap (that can be deactivated by manually adding a [SetEnvironment]).

Currently a maximum of 8 Pointlights can be used in your scene. For example by daisychaining them right before your graph goes into the camera.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command) | — |
| **Position** (Vector3) | Changes the position of the PointLight:<br/><br/>X (-left / +right), <br/>Y (-down / +up), <br/>Z (-forward / +backwards) |
| **Intensity** (Single) | Increases or decreases the intensity of the light in relation to its decay.<br/> |
| **Color** (Vector4) | Sets the color of the light. |
| **IsEnabled** (Boolean) | Turns the PointLight On / Off |
| **ShowGizmo** (GizmoVisibility) | Can be used to override the "Toggle gizmos and floor grid" setting in the Output Window.<br/><br/>- Off: The gizmo will always be invisible<br/>- On: The Gizmo will always be visible<br/>- IfSelected: The Gizmo will only be visible when the PointLight Op is selected<br/>- Inherit: The visibility of the Gizmo is controlled by the "Toggle gizmos and floor grid" setting in the Output Window.<br/> |
| **GizmoSize** (Single) | — |
| **Decay** (Single) | Controls how far the light shines into the scene.<br/><br/>A low decay setting means the light is shining far.<br/>A high decay setting means the light reaches a short distance. |
| **Range** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |

