# Lib.volume

## Sub-namespaces

- [Lib.volume.analyze](analyze/README.md)
- [Lib.volume.convert](convert/README.md)
- [Lib.volume.fields](fields/README.md)
- [Lib.volume.generate](generate/README.md)
- [Lib.volume.gpu](gpu/README.md)
- [Lib.volume.io](io/README.md)
- [Lib.volume.modify](modify/README.md)
- [Lib.volume.render](render/README.md)

## Operators

- [**LoadVdbExample**](LoadVdbExample.md) — [LoadVdb] reads OpenVDB files as written by Houdini, Blender or TiXL itself. The bunny is a level set: made coarser with [VolumeResample], meshed and scaled down. The plume is a fog volume: [VolumeToNanoVdb] uploads it and [VdbDensityField] makes its density a field for [RaymarchField]. [VolumeInfo] reports what a file contains.
- [**NanoVdbRenderExample**](NanoVdbRenderExample.md) — [NanoVdbRender] raymarches three NanoVDB grids at once: a reflective liquid, smoke and fire. Each starts as a [VolumePrimitive] - a slab, a ball and a capsule - roughened by [VolumeNoise] whose offset drifts, and is uploaded to the GPU with [VolumeToNanoVdb].
- [**VdbToSdfFieldExample**](VdbToSdfFieldExample.md) — [VdbToSdfField] makes a volume's signed distances a field, so it mixes with every SDF operator. Here a torus volume is blended smoothly with an animated [SphereSDF] by [CombineSDF], placed on an SDF floor and raymarched. Nothing is meshed.
- [**VolumeCsgExample**](VolumeCsgExample.md) — [VolumeCsg] combines level sets like a sculptor: Union merges, Intersection keeps the overlap, Difference carves. Read the row from left to right: a rounded box intersected with a sphere gives the classic CSG body, three bars joined with Union make the drill, and the body minus the drill is the result on the right.
- [**VolumeFromMeshExample**](VolumeFromMeshExample.md) — Left: the word TiXL as geometry becomes a level set with [VolumeFromMesh], and [VolumeMorphology] dilates it into soft bubble letters. Middle: scattered points are splatted into a density volume with [VolumeFromPoints], a metaball cloud. Right: a noise field is sampled over a box with [FieldToVolume].
- [**VolumeNoiseExample**](VolumeNoiseExample.md) — Three stages of one chain, left to right. [VolumeNoise] in Displace mode pushes the sphere's surface along fractal noise. [VolumeMorphology] with Dilate grows it outwards, which swells the bumps and closes the dents, and [VolumeFilter] smooths the voxels, which softens what is left.
- [**VolumePrimitiveExample**](VolumePrimitiveExample.md) — [VolumePrimitive] builds a shape directly as a level set: a sparse grid of signed distances that is exact near the surface. Eight primitives stand on the floor here, each meshed with [VolumeToMesh] and drawn lit.
- [**VolumeShakingWaterExample**](VolumeShakingWaterExample.md) — The container is an open box: a box minus a slightly smaller box with [VolumeCsg] Difference. The water is a slab displaced by [VolumeNoise] and clipped by Intersection to the inside of the box. Two [OscillateVec3] make the box shake and the water slosh.
- [**VolumeToPointsExample**](VolumeToPointsExample.md) — One torus volume, three outputs side by side. Left: [VolumeToMesh] extracts triangles. Middle: [VolumeToPoints] turns the voxels near the surface into points, drawn together with the boxes of the volume's sparse tree from [DrawVdbBounds] - only where there is surface is there data. Right: [VolumeToTexture3D] bakes a dense 3D texture and [VolumeField] makes it a field that [RaymarchField] draws.

---

*Auto-generated from the operator library.*
