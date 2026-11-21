# Lib.physics

## Operators

- [**MeshToGeometryExample**](MeshToGeometryExample.md) — [MeshToGeometry] reads a GPU mesh back as geometry, so meshes from [TorusMesh], [IcosahedronMesh], [LoadGltf] and every other mesh operator can collide. The readback only happens when the mesh changes.
- [**PhysicsSdfFieldExample**](PhysicsSdfFieldExample.md) — The ground here is not a mesh at all: it is an SDF field, a formula on the GPU, drawn with [RaymarchField]. Physics runs on the CPU, so the field reaches it by being sampled: [GridPoints] lays a grid over it, [SampleFieldPoints] writes the field's distance into every point, [ReadPointsFromGpu] brings them to the CPU and [SdfPointsToGeometry] extracts the surface as the Geometry of a static [RigidBody].
- [**PhysicsToVolumeExample**](PhysicsToVolumeExample.md) — Physics output feeds the volume operators too. The world's poses are read back with [ReadPointsFromGpu], [VolumeFromPoints] splats them into a density volume and [VolumeToMesh] extracts one blobby surface around all of them, every frame. Think of it as a metaball or liquid look on top of rigid bodies.
- [**PhysicsVolumeGravityExample**](PhysicsVolumeGravityExample.md) — A volume can be a force as well as a collider. [VolumeToField] turns the torus volume into a vector field, its gradient, and a [RigidForce] of type Field samples that field at every body. Negative Strength pulls the boxes towards the surface instead of pushing them away. The same volume, meshed with [VolumeToMesh], is the collider they land on. World gravity is zero.
- [**PhysicsVolumesExample**](PhysicsVolumesExample.md) — Any volume becomes a collider through [VolumeToMesh]. On the left, the bunny is loaded with [LoadVdb], made coarser with [VolumeResample] and scaled down; on the right, a bowl is carved out of a block with [VolumeCsg]. Balls rain on both.
- [**PhysicsWorld**](PhysicsWorld.md) — Simulates rigid bodies with BepuPhysics and outputs their poses as a point buffer.
- [**PhysicsWorldExample**](PhysicsWorldExample.md) — The basics of the physics package: a [PhysicsWorld] simulates the bodies that [RigidBody] operators describe.
- [**RigidBody**](RigidBody.md) — Declares rigid bodies for [PhysicsWorld].
- [**RigidBodyExample**](RigidBodyExample.md) — [RigidBody] turns any geometry into a collider. Shape Auto splits concave geometry into several convex hulls, so the rings keep their holes and slide down the posts they land over, and the letters keep their counters.
- [**RigidBodyGeometry**](RigidBodyGeometry.md) — Returns the simulated bodies of one or more [RigidBody] operators as geometry.
- [**RigidBodyGeometryExample**](RigidBodyGeometryExample.md) — [VoronoiFracture] cuts a cube into cells; a [RigidBody] makes every cell a body, so the cube breaks apart when it lands. [RigidBodyGeometry] returns the pieces as geometry, ready for [GeometryToMesh] or any other geometry operator.
- [**RigidBodyPoints**](RigidBodyPoints.md) — The poses of the bodies of one [RigidBody], picked out of a [PhysicsWorld].
- [**RigidBodyPointsExample**](RigidBodyPointsExample.md) — The car is a chassis and four cylinder wheels from a 2 x 2 point grid. One Hinge joint with AutoAnchor fits all four wheels at once and its MotorSpeed drives them.
- [**RigidForce**](RigidForce.md) — Declares a force for [PhysicsWorld].
- [**RigidForceExample**](RigidForceExample.md) — [RigidForce] pushes the dynamic bodies of a world. Three continuous forces make a whirlwind: a Vortex swirls the boxes around the vertical axis, a Radial force keeps them near the centre and a Directional force lifts them against gravity. A fourth force is an impulse: it fires once each time its Trigger is pressed.
- [**RigidJoint**](RigidJoint.md) — Declares a constraint between two bodies.
- [**RigidJointExample**](RigidJointExample.md) — [RigidJoint] links bodies. Three set-ups show its modes side by side.

---

*Auto-generated from the operator library.*
