# RigidJointExample

*in [Lib.physics](README.md)*

[RigidJoint] links bodies. Three set-ups show its modes side by side.

The rope is a line of points joined link to link by one joint in Chain mode, and pinned in place by its first link with a second joint in World mode. The seesaw is a plank on a Hinge around a kinematic pivot; a crate drops on one end and launches the other. The net joins every two knots that start close together, with Mode Nearby, and drapes over a ball.

AKA: constraint, rope, chain, hinge, ball socket, cloth, net, seesaw

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- A static body cannot be a joint anchor; use a Kinematic one.
- Higher Frequency makes joints stiffer; ropes and nets like more Substeps on the World.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


