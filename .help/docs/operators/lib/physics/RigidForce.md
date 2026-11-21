# RigidForce

*in [Lib.physics](README.md)*

Declares a force for [PhysicsWorld].

Outputs a description, like [RigidBody]: the world applies it to its dynamic bodies while it steps. A continuous force accelerates them every step. With IsImpulse on it changes their velocity once each time Trigger turns on, which suits beats and other events.

AKA: wind, attractor, repel, explosion, vortex, impulse, kick, field force

Tips
- Connect any vector field to Field and choose the Field type to steer bodies with it.
- A negative Strength on a Radial impulse is an explosion.
- Resting bodies are woken by a force that is strong enough to move them.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Position** (Vector3) | Centre of a Radial force, or a point on the axis of a Vortex. |
| **Field** (VectorField) | Vector field for the Field type, sampled at each body's position and multiplied by Strength. |
| **CollisionGroup** (Int32) | Only bodies with this collision group are affected. 0 affects all bodies. |
| **Strength** (Single) | Acceleration in units per second squared. For an impulse, the velocity change in units per second. |
| **Radius** (Single) | Reach of a Radial or Vortex force around its centre or axis. 0 is unlimited. |
| **ForceType** (Int32) | Directional pushes along Direction. Radial pulls towards Position, or away from it with a negative Strength. Vortex swirls around the axis through Position along Direction. Field samples the connected vector field at each body. |
| **Enabled** (Boolean) | Turns the force off without deleting the node. |
| **ScaleByMass** (Boolean) | Divide the effect by each body's mass, so heavy bodies react less. Off moves everything alike. |
| **Trigger** (Boolean) | Fires the impulse each time it turns on. |
| **Falloff** (Single) | How much the force fades towards Radius: 0 keeps full strength up to the edge, 1 fades to nothing at the edge. |
| **IsImpulse** (Boolean) | Apply once per Trigger instead of continuously: a kick, not a wind. |
| **Direction** (Vector3) | Direction of a Directional force, or the axis of a Vortex. Its length does not matter. |

## Outputs

| Name | Type |
|---|---|
| **OutForce** | T3.Core.DataTypes.PhysicsForce |


