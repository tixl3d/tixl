# OrbitCamera

*in [Lib.render.camera](README.md)*

Provides common camera movement utilities for an easy dynamic camera setup.

This operator was previously known as RandomCamera.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | Scene input |
| **CameraTargetPosition** (Vector3) | Position of the camera target |
| **FOV** (Single) | Defines the field of view (fov) or focal length of the camera in degrees |
| **DistanceToTarget** (Single) | Distance of the camera to the target |
| **SpinRate** (Single) | Speed at which the camera rotates |
| **SpinOffset** (Single) | Value in degrees that is added to the "SpinRate" |
| **OrbitAngleAndWobble** (Vector2) | X = The height at which the camera rotates around the target<br/>Y = Defines how much may be deviated from the first value at random |
| **AimRollAngleAndWobble** (Vector2) | X = Rolls the camera to the right or left<br/>Y = Defines how much may be deviated from the first value at random |
| **AimPitchAngleAndWobble** (Vector2) | X = Camera Pitch<br/>Y = Defines how much may be deviated from the first value at random |
| **AimYawAngleAndWobble** (Vector2) | X = Turns the camera view to the left or right<br/>Y = Defines how much may be deviated from the first value at random |
| **SpinAngleAndWobble** (Vector2) | X = Same as "SpinOffset"<br/>Y = Defines how much may be deviated from the first value at random |
| **WobbleSpeed** (Single) | Defines how quickly the Y values vary in the "Wobble Settings". |
| **WobbleComplexity** (Int32) | Defines the complexity with which the random values fluctuate |
| **Damping** (Single) | Smoothes the random wobbling |
| **Seed** (Int32) | Random seed to manipulate random values |
| **NearFarClip** (Vector2) | Defines the start and end of the area in which objects are rendered by the camera. |
| **RotationOffset** (Vector3) | Camera Rotation offset |
| **PositionOffset** (Vector3) | — |
| **AspectRatio** (Single) | Defines the aspect ratio |
| **Up** (Vector3) | Defines the Up-Vector of the camera |
| **OverrideTime** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Command |
| **Reference** | System.Object |

