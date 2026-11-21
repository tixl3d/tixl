# RigidBodyPoints

*in [Lib.physics](README.md)*

The poses of the bodies of one [RigidBody], picked out of a [PhysicsWorld].

The world publishes all its bodies in one point buffer, which [DrawMeshAtPoints] can only draw with one mesh. Connect the world's OutBuffer and one body description here to get a buffer with just that description's bodies, and draw each kind of body with its own mesh.

AKA: body poses, filter bodies, per body points, body transform

Tips
- Static and kinematic bodies are included, so a moving kinematic platform can be drawn this way too.
- With several fractured objects in one world, give each its own [RigidBodyPoints] before [DrawMeshChunksAtPoints].
- Count is the number of bodies the description stands for.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Body** (PhysicsBody) | The [RigidBody] whose bodies to pick. |
| **World** (BufferWithViews) | The OutBuffer of the [PhysicsWorld] that simulates the body. Connecting it also makes sure the world has stepped before the poses are read. |

## Outputs

| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.BufferWithViews |
| **Count** | System.Int32 |


