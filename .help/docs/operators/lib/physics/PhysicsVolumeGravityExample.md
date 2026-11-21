# PhysicsVolumeGravityExample

*in [Lib.physics](README.md)*

A volume can be a force as well as a collider. [VolumeToField] turns the torus volume into a vector field, its gradient, and a [RigidForce] of type Field samples that field at every body. Negative Strength pulls the boxes towards the surface instead of pushing them away. The same volume, meshed with [VolumeToMesh], is the collider they land on. World gravity is zero.

AKA: gravity well, attractor, magnet, field force, volume gradient

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- Any [VectorField] works as a force: noise, curl, or a field built from SDF operators.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


