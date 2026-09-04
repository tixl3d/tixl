# Camera

*in [Lib.render.camera](README.md)*

Sets the camera for the subgraph.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | — |
| **Position** (Vector3) | X (-left / +right), Y (-down / +up), Z (-forward / +backwards) coordinates of the current view position. <br/><br/>The default z-position is 2.41 behind the centered scene origin so that a [Layer2d] with a size of 2 precisely fills the screen height for a Field of View (FOV) of 45°. |
| **Target** (Vector3) | The view target of the camera. The line between the Position and the Target defines the view direction. |
| **FieldOfView** (Single) | Defines the field of view (FOV) or focal length of the camera in degrees |
| **Roll** (Single) | Rotates the camera right/left |
| **PositionOffset** (Vector3) | — |
| **AlsoOffsetTarget** (Boolean) | — |
| **RotationOffset** (Vector3) | — |
| **LensShift** (Vector2) | — |
| **ClipPlanes** (Vector2) | Defines the start and end of the area in which objects are rendered by the camera. |
| **AspectRatio** (Single) | Enforce a fixed aspect ratio for the camera view. <br/>For values smaller or equal to 0 this setting is ignored and the current view's aspect ratio is automatically used. When setting this parameter to a positive window, the content gets stretched with the aspect of the output window. |
| **Up** (Vector3) | Defines a helping vector to control the rotation of the camera. If the view direction approaches the Up-Vector, camera motion becomes unstable because of gimbal lock.<br/><br/>This needs to be adjusted in cases where the camera needs to point directly up or down. |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **Reference** | System.Object |

