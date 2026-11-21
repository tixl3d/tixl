# PhysicsWorldExample

*in [Lib.physics](README.md)*

The basics of the physics package: a [PhysicsWorld] simulates the bodies that [RigidBody] operators describe.

Five balls show Bounciness from 0 to 1, a ball tips over a row of dominoes made from points, and capsules and cylinders tumble onto the floor. Every impact is reported by the world's Contacts output and drawn as a spark. [RigidBodyGeometry] hands the simulated bodies back as geometry, so each one is drawn as exactly the shape it collides as.

AKA: physics, rigid body, simulation, bepu, bounce, domino, collision

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- Select a ball and change Bounciness or Friction while it plays.
- A body without Geometry is a primitive; Shape picks box, sphere, capsule or cylinder.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


