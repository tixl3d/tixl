# RigidForceExample

*in [Lib.physics](README.md)*

[RigidForce] pushes the dynamic bodies of a world. Three continuous forces make a whirlwind: a Vortex swirls the boxes around the vertical axis, a Radial force keeps them near the centre and a Directional force lifts them against gravity. A fourth force is an impulse: it fires once each time its Trigger is pressed.

AKA: wind, vortex, tornado, attractor, gravity well, explosion, impulse, force field

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- Negative Strength turns a radial pull into a push.
- ScaleByMass makes heavy bodies react less; Field samples a [VectorField], e.g. from [VolumeToField].

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


