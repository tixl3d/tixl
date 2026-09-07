# ActionCamera

*in [Lib.render.camera](README.md)*

Allows to interactive control a camera starting from a giving reference camera.

Also know as: FlyThrough, GameControlled camera, walk through, POV, Point of View.

Also see [BlendCameras].



## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ReferenceCamera** (Object Required) | A link to a camera that defines the original starting point. |
| **BlendToReferenceCamera** (Single) | Allow to smoothly transition back to the reference camera. |
| **TriggerReset** (Boolean) | Jump back to the reference camera. Same as Blending with 1. |
| **Speed** (Single) | Allows to adjust the movement speed. |
| **Forward** (Single) | — |
| **Sideways** (Single) | Currently computed by forward and world space up. |
| **UpDown** (Single) | Move along the world space up vector. |
| **RotationSpeed** (Single) | Allows to adjust speed of rotation. |
| **Yaw** (Single) | Rotate around y-axis |
| **Pitch** (Single) | Rotate around side vector (Forward cross World up) |
| **Roll** (Single) | Additional roll parameter applied after movement. Does not have an affecto to movement. |
| **FOV** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Reference** | System.Object |

