# PhysicsSdfFieldExample

*in [Lib.physics](README.md)*

The ground here is not a mesh at all: it is an SDF field, a formula on the GPU, drawn with [RaymarchField]. Physics runs on the CPU, so the field reaches it by being sampled: [GridPoints] lays a grid over it, [SampleFieldPoints] writes the field's distance into every point, [ReadPointsFromGpu] brings them to the CPU and [SdfPointsToGeometry] extracts the surface as the Geometry of a static [RigidBody].

The hilly terrain is sampled once. The blob is a plain sphere, so its collider is a Kinematic sphere [RigidBody] driven by the same [AnimVec3] as the field: it follows the blob exactly and shoves the bodies around. Boxes come from [CubeGeometry], tori and icosahedra from GPU meshes through [MeshToGeometry]. Raymarching writes depth, so the meshes sit correctly in the raymarched scene.

AKA: sdf collider, field collider, raymarch, implicit surface, signed distance, terrain

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- The sample grid sets the collider's detail: Count times Size must cover the shape.
- Reading a grid back and rebuilding a mesh collider every frame is expensive. Moving shapes that are spheres, boxes or capsules collide best as Kinematic primitives that follow the same motion, like the blob.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


