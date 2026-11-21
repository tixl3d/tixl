# RigidBodyGeometry

*in [Lib.physics](README.md)*

Returns the simulated bodies of one or more [RigidBody] operators as geometry.

This is the way back from physics into the geometry operators. A [RigidBody] made from geometry gives that same geometry back with every part moved by its body, so one geometry describes the object, is the collider and is what you draw. A primitive [RigidBody] gives a box, sphere, capsule or cylinder of its own size, so it needs no separate mesh either.

Every body is one part of the result. Connect [GeometryToMesh] to draw it, or any other geometry operator.

AKA: simulated geometry, physics mesh, pose geometry, bake physics

Tips
- The geometry is rebuilt every frame on the CPU. For many identical bodies, [RigidBodyPoints] with [DrawMeshAtPoints] is cheaper.
- Connect several [RigidBody] outputs to Body to get them as one geometry. Joined bodies keep positions, faces, parts, normals and part colors; other attributes are kept only for a single body.
- Bodies spawned from points carry the point color as a Color part attribute.
- Turn on ShowColliders to see the hulls a concave body was split into.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **ShowColliders** (Boolean) | Draws bodies made from geometry as the convex hulls they collide as, each hull in a face color of its own, instead of as their geometry. Use it to check how a concave shape was split. |
| **Body** (PhysicsBody) | The [RigidBody] operators whose simulated bodies to return as geometry. Several are joined into one geometry, in connection order. |
| **World** (BufferWithViews) | The OutBuffer of [PhysicsWorld]. It makes the world step before the poses are read. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.MeshGeometry |
| **Count** | System.Int32 |


