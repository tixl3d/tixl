# RigidBodyPointsExample

*in [Lib.physics](README.md)*

The car is a chassis and four cylinder wheels from a 2 x 2 point grid. One Hinge joint with AutoAnchor fits all four wheels at once and its MotorSpeed drives them.

[RigidBodyPoints] picks one body description's poses out of the world as a point buffer. That is the cheap way to draw many identical bodies: one mesh repeated at every point with [RepeatMeshAtPoints], instead of rebuilding geometry every frame.

AKA: vehicle, car, wheels, motor, axle, instancing, crates

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- Bodies with the same CollisionGroup pass through each other; the car parts share one so the wheels can touch the chassis.
- Negative MotorSpeed drives backwards.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


