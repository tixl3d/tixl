# RigidBody

*in [Lib.physics](README.md)*

Declares rigid bodies for [PhysicsWorld].

Outputs a description, not a live body: the world owns the simulation state. On its own this is one primitive body. Connect Points for one body per point, and Geometry to collide as that geometry: every geometry part becomes a body (try [VoronoiFracture]), the whole geometry becomes one body (Pieces WholeObject), or the whole geometry sits at each point. Concave geometry such as a torus, a cup or a letter collides as several convex hulls, so its holes stay open. Bodies made from geometry weigh their volume times Density.

Use Kind Static for colliders that never move, and Kind Kinematic for infinite-mass bodies that follow the Position input or serve as joint anchors.

AKA: collider, physics body, box, sphere, capsule, cylinder, convex hull, convex decomposition, compound, mesh collider, fracture, instances

Tips
- Static and kinematic bodies ignore Mass and Density.
- Shape Auto picks the right collider for connected geometry; ConvexHull is the fastest choice for shapes that are already convex.
- Concave geometry is split in the background whenever it changes; a new body appears once its hulls are ready. Avoid animating the geometry of a dynamic body.
- [RigidBodyGeometry] with ShowColliders draws the hulls a body collides as.
- Do not let a joint partner overlap its anchor collider; the contact pushes them apart.
- To collide with a volume, connect [VolumeToMesh] to Geometry and set Kind to Static.
- For a mesh from [LoadGltf], [CubeMesh] or another mesh operator, insert [MeshToGeometry].
- [RigidBodyGeometry] returns the simulated bodies as geometry, ready for [GeometryToMesh].

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Size** (Vector3) | Full extents of a box collider. |
| **Enabled** (Boolean) | Removes the body from the simulation when off, including its joints. |
| **MassMode** (Int32) | How a dynamic body gets its mass. Auto weighs geometry bodies by their volume times Density, so a small fracture shard is lighter than a big chunk, and gives primitives the fixed Mass. Fixed always uses Mass; FromDensity always uses the volume. |
| **GravityScale** (Single) | Multiplier on world gravity for this body. 0 makes it float. |
| **Friction** (Single) | Contact friction. Pairs combine as the geometric mean. |
| **CollisionGroup** (Int32) | Bodies with the same non-zero group pass through each other. Use it for the links of a rope or the pieces of one object. 0 collides with everything. |
| **AngularVelocity** (Vector3) | Initial angular velocity in radians per second. |
| **Position** (Vector3) | Initial world position. A change teleports the body. |
| **Shape** (Int32) | Collider shape. Auto is a box without Geometry; with Geometry it is the exact triangles for static and kinematic bodies and, for dynamic bodies, a convex hull that is split into several hulls where the shape is concave (see MaxHulls). ConvexHull wraps every part in one hull, ConvexDecomposition always splits, and Mesh is the exact triangles. Box, Sphere, Capsule and Cylinder are primitives; with Geometry connected they act like Auto. |
| **Kind** (Int32) | Dynamic bodies fall and collide; Static bodies never move; Kinematic bodies have infinite mass and follow their transform. |
| **Geometry** (MeshGeometry) | Optional. The bodies collide as this geometry instead of a primitive. Pieces decides whether every part becomes its own body, which is how [VoronoiFracture] pieces fall apart, or the whole geometry is one body; with Points the whole geometry is placed at each point. GPU meshes need [MeshToGeometry] first. |
| **Bounciness** (Single) | Restitution approximation: 0 is dead, 1 is very bouncy. |
| **Rotation** (Vector3) | Initial rotation in degrees (yaw around Y, pitch around X, roll around Z). |
| **Mass** (Single) | Mass of a dynamic body in kilograms when MassMode is Fixed, or Auto without Geometry. Ignored for static and kinematic bodies. |
| **LinearDamping** (Single) | Drag applied to linear velocity per second. |
| **AngularDamping** (Single) | Drag applied to angular velocity per second. |
| **Points** (StructuredList) | Optional. One body per point: the point's position, rotation and scale place and size the body relative to Position and Rotation, and its color is passed through to the world's output. GPU points need [ReadPointsFromGpu] first. |
| **Radius** (Single) | Radius of sphere, capsule and cylinder colliders. |
| **Density** (Single) | Mass per cubic unit when the mass comes from the volume. 1 gives a unit cube a mass of 1. |
| **Length** (Single) | Length of the cylindrical section of capsule and cylinder colliders. |
| **MaxHulls** (Int32) | Most convex hulls one geometry part is split into by the Auto and ConvexDecomposition shapes. More hulls follow a concave shape closer and cost more to build and simulate; 1 turns the splitting off. |
| **Pieces** (Int32) | What the parts of the connected geometry become. EachPartIsABody lets every part move on its own, like the cells of a [VoronoiFracture]. WholeObject keeps all parts together as one body, like the pieces of an imported model. With Points the whole geometry is always one body per point. |
| **LinearVelocity** (Vector3) | Initial linear velocity. Changing it applies the new velocity. |

## Outputs

| Name | Type |
|---|---|
| **OutBody** | T3.Core.DataTypes.PhysicsBody |


