# PhysicsToVolumeExample

*in [Lib.physics](README.md)*

Physics output feeds the volume operators too. The world's poses are read back with [ReadPointsFromGpu], [VolumeFromPoints] splats them into a density volume and [VolumeToMesh] extracts one blobby surface around all of them, every frame. Think of it as a metaball or liquid look on top of rigid bodies.

None of this holds up a frame: the read-back is asynchronous and both volume operators work on a background thread, so the skin trails the balls by a frame or two while playback stays smooth.

AKA: metaballs, blobby, liquid, skin, splat, points to volume

Tips
- Playback must run for the simulation to advance; press Reset on the World to start over.
- A larger Radius on [VolumeFromPoints] melts the balls together; a smaller VoxelSize gives a smoother skin and costs more.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


