"""Generates the volume example graphs in Operators/Examples/Symbols/Lib/volume.

Run from anywhere:  python3 Scripts/example-scenes/volumes.py <repository root>
The editor must not have the examples loaded while they are rewritten, or its next save overwrites them.
"""
import sys
import gen
from gen import PALETTE as C, SECTION_COLORS as S, mesh_view, floor, render_rig

TIPS_COMMON = ("- Smaller VoxelSize means sharper surfaces and more memory and time; start coarse and refine.\n"
               "- Level sets store signed distance in world units (negative inside); fog volumes store density.")

def Scene(name, summary, description):
    return gen.Scene(name, summary, description, folder='volume')


PRIMITIVES = ['Sphere', 'Box', 'RoundedBox', 'Torus', 'Capsule', 'Cylinder', 'Cone', 'Octahedron']


def volume_primitive():
    s = Scene('VolumePrimitiveExample', 'Every volume primitive side by side: the building blocks of the volume package.',
              "[VolumePrimitive] builds a shape directly as a level set: a sparse grid of signed distances that is exact near the "
              "surface. Eight primitives stand on the floor here, each meshed with [VolumeToMesh] and drawn lit.\n\n"
              "A level set is not a mesh: it can be combined with [VolumeCsg], displaced with [VolumeNoise], grown with "
              "[VolumeMorphology] and only turned into triangles at the end.\n\n"
              "AKA: vdb, openvdb, level set, sdf grid, voxel shape, primitive\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Size is the full extent; a sphere reads only its X.\n"
              "- BandWidth is how many voxels of exact distance surround the surface; widen it before heavy morphology.")
    sizes = {'Sphere': (1.1, 1.1, 1.1), 'Box': (0.95, 0.95, 0.95), 'RoundedBox': (1.0, 1.0, 1.0), 'Torus': (1.2, 0.36, 1.0),
             'Capsule': (0.6, 1.2, 0.6), 'Cylinder': (0.8, 1.1, 0.8), 'Cone': (0.95, 1.1, 0.95), 'Octahedron': (1.1, 1.1, 1.1)}
    colors = [C['orange'], C['teal'], C['yellow'], C['red'], C['blue'], C['violet'], C['white'], C['orange']]
    draws = [floor(s, 'floor', 2, 0, height=-0.6)]
    keys = []
    for index, shape in enumerate(PRIMITIVES):
        x = -2.4 + (index % 4) * 1.6
        z = -0.9 if index < 4 else 0.9
        key = shape[0].lower() + shape[1:]
        row = 1 + index
        s.op(key, 'VolumePrimitive', 0, row, name=shape, Shape=index, Size=sizes[shape], Center=(x, 0.0, z), VoxelSize=0.03, BandWidth=3.0)
        s.op(key + 'Surface', 'VolumeToMesh', 1, row, name=shape + 'Surface', Isovalue=0.0)
        s.link(key + '.Volume', key + 'Surface.Volume')
        draws.append(mesh_view(s, key + 'View', key + 'Surface.Geometry', 2, row, colors[index], shape))
        keys += [key, key + 'Surface']
    render_rig(s, draws, 6, camera=(0.0, 3.4, 6.5), target=(0.0, -0.1, 0.0), light=(3.0, 7.0, 4.0), fog=40.0)
    s.section('1  Eight primitives as level sets', keys, S['source'])
    s.section('2  Meshed and drawn', [k for k in s.ops if 'View' in k], S['draw'])
    s.tour('sphere', 'A [VolumePrimitive] is a shape made of voxels: signed distances to its surface, stored only near it.', 'Info')
    s.tour('torus', 'Shape picks the primitive; Size is its full extent and Center places it.', 'InfoFor', 'Shape')
    s.tour('torus', 'VoxelSize is the grid resolution. Halve it and the surface gets sharper, at four times the memory.', 'InfoFor', 'VoxelSize')
    s.tour('torusSurface', '[VolumeToMesh] extracts the surface where the distance is Isovalue, 0 for a level set.', 'InfoFor', 'Isovalue')
    s.tour('cone', 'Try changing a VoxelSize to 0.1 to see how coarse voxels round everything off.', 'CallToAction', 'VoxelSize')
    return s


def volume_csg():
    s = Scene('VolumeCsgExample', 'Constructive solid geometry with volumes: a rounded box intersected with a sphere, then drilled through.',
              "[VolumeCsg] combines level sets like a sculptor: Union merges, Intersection keeps the overlap, Difference carves. "
              "Read the row from left to right: a rounded box intersected with a sphere gives the classic CSG body, three bars "
              "joined with Union make the drill, and the body minus the drill is the result on the right.\n\n"
              "Because every step is again a level set, the steps chain without ever building a mesh in between.\n\n"
              "AKA: boolean, union, intersection, difference, subtract, carve, csg\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Volumes with different voxel sizes are combined into the finer grid.")
    s.op('roundedBox', 'VolumePrimitive', 0, 1, name='RoundedBox', Shape=2, Size=(1.6, 1.6, 1.6), VoxelSize=0.03, BandWidth=3.0)
    s.op('sphere', 'VolumePrimitive', 0, 2, name='Sphere', Shape=0, Size=(2.1, 2.1, 2.1), VoxelSize=0.03, BandWidth=3.0)
    s.op('body', 'VolumeCsg', 1, 1, name='Intersect', Operation=1)
    s.link('roundedBox.Volume', 'body.VolumeA')
    s.link('sphere.Volume', 'body.VolumeB')
    s.op('barY', 'VolumePrimitive', 0, 4, name='BarY', Shape=5, Size=(0.75, 2.6, 0.75), VoxelSize=0.03, BandWidth=3.0)
    s.op('barX', 'VolumePrimitive', 0, 5, name='BarX', Shape=1, Size=(2.6, 0.6, 0.6), VoxelSize=0.03, BandWidth=3.0)
    s.op('barZ', 'VolumePrimitive', 0, 6, name='BarZ', Shape=1, Size=(0.6, 0.6, 2.6), VoxelSize=0.03, BandWidth=3.0)
    s.op('drillXY', 'VolumeCsg', 1, 4, name='UnionXY', Operation=0)
    s.op('drill', 'VolumeCsg', 2, 5, name='UnionDrill', Operation=0)
    s.link('barY.Volume', 'drillXY.VolumeA')
    s.link('barX.Volume', 'drillXY.VolumeB')
    s.link('drillXY.Volume', 'drill.VolumeA')
    s.link('barZ.Volume', 'drill.VolumeB')
    s.op('result', 'VolumeCsg', 3, 2, name='Subtract', Operation=2)
    s.link('body.Volume', 'result.VolumeA')
    s.link('drill.Volume', 'result.VolumeB')
    for key, source in [('bodySurface', 'body'), ('drillSurface', 'drill'), ('resultSurface', 'result')]:
        s.op(key, 'VolumeToMesh', 4, {'bodySurface': 1, 'drillSurface': 4, 'resultSurface': 2}[key], name=key[0].upper() + key[1:])
        s.link(source + '.Volume', key + '.Volume')
    draws = [floor(s, 'floor', 5, 0, height=-1.1),
             mesh_view(s, 'bodyView', 'bodySurface.Geometry', 5, 1, C['teal'], 'Body', translation=(-3.0, 0.0, 0.0)),
             mesh_view(s, 'drillView', 'drillSurface.Geometry', 5, 4, C['white'], 'Drill', translation=(0.0, 0.0, 0.0)),
             mesh_view(s, 'resultView', 'resultSurface.Geometry', 5, 2, C['orange'], 'Result', translation=(3.0, 0.0, 0.0))]
    render_rig(s, draws, 9, camera=(0.5, 3.0, 8.5), target=(0.0, 0.0, 0.0), light=(3.0, 7.0, 5.0), fog=40.0)
    s.section('1  Body: rounded box AND sphere', ['roundedBox', 'sphere', 'body'], S['source'])
    s.section('2  Drill: three bars joined', ['barY', 'barX', 'barZ', 'drillXY', 'drill'], S['bodies'])
    s.section('3  Body minus drill', ['result', 'bodySurface', 'drillSurface', 'resultSurface'], S['world'])
    s.tour('body', 'Intersection keeps only what is inside both: the rounded box with its corners cut off by the sphere.', 'InfoFor', 'Operation')
    s.tour('drill', 'Union merges the three bars into one cutting tool.', 'InfoFor', 'Operation')
    s.tour('result', 'Difference carves VolumeB out of VolumeA: the body with three square and round holes.', 'InfoFor', 'Operation')
    s.tour('resultSurface', 'Only the end of the chain is meshed; every step before it stays a level set.', 'InfoFor')
    s.tour('body', 'Switch the first CSG to Union or Difference and watch the whole chain update.', 'CallToAction', 'Operation')
    return s


def volume_noise():
    s = Scene('VolumeNoiseExample', 'Organic shapes: a sphere displaced by noise, then grown and smoothed, step by step.',
              "Three stages of one chain, left to right. [VolumeNoise] in Displace mode pushes the sphere's surface along fractal "
              "noise. [VolumeMorphology] with Dilate grows it outwards, which swells the bumps and closes the dents, and "
              "[VolumeFilter] smooths the voxels, which softens what is left.\n\n"
              "AKA: displace, noise, blob, organic, smooth, blur, erode, dilate, open, close\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Animated volumes are rebuilt on the CPU every frame; keep their voxels coarse.")
    s.op('sphere', 'VolumePrimitive', 0, 1, name='Sphere', Shape=0, Size=(1.8, 1.8, 1.8), VoxelSize=0.035, BandWidth=8.0)
    s.op('noise', 'VolumeNoise', 1, 1, name='Displace', Mode=2, NoiseType=0, Fractal=1, Octaves=4, Frequency=1.6, Amplitude=5.0, Seed=7)
    s.link('sphere.Volume', 'noise.Volume')
    s.op('open', 'VolumeMorphology', 2, 1, name='Grow', Operation=0, Radius=0.15)
    s.link('noise.Result', 'open.Volume')
    s.op('smooth', 'VolumeFilter', 3, 1, name='Smooth', FilterType=0, Radius=2.0, Iterations=2)
    s.link('open.Result', 'smooth.Volume')
    for key, source, row in [('noiseSurface', 'noise', 2), ('openSurface', 'open', 3), ('smoothSurface', 'smooth', 4)]:
        s.op(key, 'VolumeToMesh', 4, row, name=key[0].upper() + key[1:])
        s.link(source + '.Result', key + '.Volume')
    draws = [floor(s, 'floor', 5, 0, height=-1.2),
             mesh_view(s, 'noiseView', 'noiseSurface.Geometry', 5, 2, C['red'], 'Displaced', translation=(-2.6, 0.0, 0.0)),
             mesh_view(s, 'openView', 'openSurface.Geometry', 5, 3, C['orange'], 'Grown'),
             mesh_view(s, 'smoothView', 'smoothSurface.Geometry', 5, 4, C['yellow'], 'Smoothed', translation=(2.6, 0.0, 0.0))]
    render_rig(s, draws, 9, camera=(0.0, 2.6, 8.0), target=(0.0, 0.0, 0.0), light=(3.0, 7.0, 5.0), fog=40.0)
    s.section('1  Displace, grow, smooth', ['sphere', 'noise', 'open', 'smooth'], S['source'])
    s.section('2  Each stage meshed', ['noiseSurface', 'openSurface', 'smoothSurface'], S['draw'])
    s.tour('noise', 'Mode Displace pushes the surface along the noise. Amplitude is in voxels and cannot reach beyond the BandWidth of the input.', 'InfoFor', 'Mode')
    s.tour('open', 'Dilate grows the surface outwards by Radius: the bumps swell and the dents between them close up. Erode, Open and Close are the other operations.', 'InfoFor', 'Operation')
    s.tour('smooth', 'A Gaussian [VolumeFilter] blurs the voxel values, which softens what is left.', 'InfoFor', 'FilterType')
    s.tour('noise', 'Connect an [AnimVec3] to Offset to make the shape flow. Every change rebuilds the chain on the CPU, so keep VoxelSize coarse while animating.', 'Tip', 'Offset')
    s.tour('noise', 'Try Fractal Ridged for sharp crests, or NoiseType Worley for cells.', 'CallToAction', 'Fractal')
    return s


def volume_from_mesh():
    s = Scene('VolumeFromMeshExample', 'Three ways into a volume: from a mesh, from points and from a field.',
              "Left: the word TiXL as geometry becomes a level set with [VolumeFromMesh], and [VolumeMorphology] dilates it into "
              "soft bubble letters. Middle: scattered points are splatted into a density volume with [VolumeFromPoints], a "
              "metaball cloud. Right: a noise field is sampled over a box with [FieldToVolume].\n\n"
              "Whatever goes in, what comes out is a volume, and every volume operator works on it.\n\n"
              "AKA: voxelize, mesh to sdf, points to volume, metaballs, splat, field to volume\n\n"
              "Tips\n" + TIPS_COMMON + "\n- A density volume is meshed at a density Isovalue, not at 0.")
    s.op('text', 'TextToCurves', 0, 1, name='Word', Text='TiXL', Size=1.1)
    s.op('letters', 'CurvesToGeometry', 1, 1, name='Letters', Depth=0.3)
    s.link('text.Curves', 'letters.Curves')
    s.op('voxelize', 'VolumeFromMesh', 2, 1, name='Voxelize', VoxelSize=0.025, BandWidth=5.0, Mode=0)
    s.link('letters.Result', 'voxelize.Geometry')
    s.op('inflate', 'VolumeMorphology', 3, 1, name='Inflate', Operation=0, Radius=0.05)
    s.link('voxelize.Volume', 'inflate.Volume')
    s.op('lettersSurface', 'VolumeToMesh', 4, 1, name='LettersSurface')
    s.link('inflate.Result', 'lettersSurface.Volume')

    s.op('points', 'ScatterPointsInVolume', 0, 3, name='Points', Count=90, Center=(0.0, 0.3, 0.0), Size=(1.4, 1.2, 1.4), Seed=4)
    s.op('splat', 'VolumeFromPoints', 2, 3, name='Splat', VoxelSize=0.03, Radius=0.28, Density=1.0, Falloff=0)
    s.link('points.ResultList', 'splat.Points')
    s.op('cloudSurface', 'VolumeToMesh', 4, 3, name='CloudSurface', Isovalue=0.5)
    s.link('splat.Volume', 'cloudSurface.Volume')

    s.op('field', 'NoiseField', 0, 5, name='NoiseField', Frequency=2.5, Octaves=3, Amplitude=1.0, Seed=4)
    s.op('sample', 'FieldToVolume', 2, 5, name='SampleField', Center=(0.0, 0.3, 0.0), Size=(1.5, 1.5, 1.5), VoxelSize=0.025, Mode=1)
    s.link('field.Result', 'sample.Field')
    s.op('fieldSurface', 'VolumeToMesh', 4, 5, name='FieldSurface', Isovalue=0.25)
    s.link('sample.Volume', 'fieldSurface.Volume')

    draws = [floor(s, 'floor', 5, 0, height=-0.45),
             mesh_view(s, 'lettersView', 'lettersSurface.Geometry', 5, 1, C['blue'], 'Letters', translation=(-4.4, -0.4, 0.0)),
             mesh_view(s, 'cloudView', 'cloudSurface.Geometry', 5, 3, C['teal'], 'Cloud', translation=(0.6, 0.0, 0.0)),
             mesh_view(s, 'fieldView', 'fieldSurface.Geometry', 5, 5, C['orange'], 'FieldShape', translation=(3.0, 0.0, 0.0))]
    render_rig(s, draws, 9, camera=(-0.5, 2.6, 8.0), target=(-0.5, 0.2, 0.0), light=(3.0, 7.0, 5.0), fog=40.0)
    s.section('1  From a mesh', ['text', 'letters', 'voxelize', 'inflate', 'lettersSurface'], S['source'])
    s.section('2  From points', ['points', 'splat', 'cloudSurface'], S['bodies'])
    s.section('3  From a field', ['field', 'sample', 'fieldSurface'], S['world'])
    s.tour('voxelize', '[VolumeFromMesh] turns closed geometry into a level set. The smallest feature needs a few voxels.', 'InfoFor', 'VoxelSize')
    s.tour('inflate', 'Dilate grows the surface outwards by Radius: crisp letters turn into bubbles.', 'InfoFor', 'Radius')
    s.tour('splat', '[VolumeFromPoints] adds a soft ball of density around every point; close balls merge.', 'InfoFor', 'Radius')
    s.tour('cloudSurface', 'Density is meshed at a threshold: lower the Isovalue for a fatter cloud.', 'InfoFor', 'Isovalue')
    s.tour('sample', '[FieldToVolume] samples any scalar field over a box; Mode chooses level set or density.', 'InfoFor', 'Mode')
    return s


def volume_outputs():
    s = Scene('VolumeToPointsExample', 'Three ways out of a volume: a mesh, a point cloud with its sparse tree, and a raymarched texture.',
              "One torus volume, three outputs side by side. Left: [VolumeToMesh] extracts triangles. Middle: [VolumeToPoints] "
              "turns the voxels near the surface into points, drawn together with the boxes of the volume's sparse tree from "
              "[DrawVdbBounds] - only where there is surface is there data. Right: [VolumeToTexture3D] bakes a dense 3D texture "
              "and [VolumeField] makes it a field that [RaymarchField] draws.\n\n"
              "AKA: volume to points, point cloud, voxels, sparse tree, raymarch, 3d texture, convert\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Meshes suit shading and physics, points suit particles, the texture suits raymarching and SDF combinations.")
    s.op('torus', 'VolumePrimitive', 0, 2, name='Torus', Shape=3, Size=(1.8, 0.6, 1.0), VoxelSize=0.03, BandWidth=3.0)
    s.op('surface', 'VolumeToMesh', 1, 0, name='Surface')
    s.link('torus.Volume', 'surface.Volume')
    s.op('points', 'VolumeToPoints', 1, 2, name='Voxels', Mode=0, Isovalue=0.0, MaxPoints=120000)
    s.link('torus.Volume', 'points.Volume')
    s.op('drawPoints', 'DrawPointsShaded', 3, 2, name='DrawVoxels', PointSize=0.02, Color=(0.4, 0.8, 1.0, 1.0))
    s.link('points.GPoints', 'drawPoints.GPoints')
    s.op('nano', 'VolumeToNanoVdb', 1, 3, name='ToNanoVdb')
    s.link('torus.Volume', 'nano.Volume')
    s.op('boundsShader', 'ComputeShader', 1, 4, name='BoundsShader', Source='Lib:shaders/volume/nanovdb-bounds.hlsl', EntryPoint='main')
    s.op('bounds', 'DrawVdbBounds', 2, 3, name='TreeBounds', Level=1, MaxNodes=4000, Capacity=64000, Color=(1.0, 0.75, 0.35, 1.0))
    s.link('nano.Buffer', 'bounds.Grid')
    s.link('boundsShader.Shader', 'bounds.Shader')
    s.op('drawBounds', 'DrawLines', 3, 3, name='DrawTree', LineWidth=0.008, Color=(1.0, 0.75, 0.35, 0.8))
    s.link('bounds.Points', 'drawBounds.GPoints')
    s.op('texture', 'VolumeToTexture3D', 1, 5, name='Bake', MaxResolution=128)
    s.link('torus.Volume', 'texture.Volume')
    s.op('volumeField', 'VolumeField', 2, 5, name='AsField', ValueScale=1.0, ValueOffset=0.0, OutsideValue=1.5)
    s.link('texture.Texture', 'volumeField.Texture')
    s.link('texture.BoundsMin', 'volumeField.BoundsMin')
    s.link('texture.BoundsMax', 'volumeField.BoundsMax')
    s.op('moveField', 'TransformField', 3, 5, name='MoveRight', Translation=(2.6, 0.0, 0.0))
    s.link('volumeField.Result', 'moveField.InputField')
    s.op('raymarch', 'RaymarchField', 4, 5, name='Raymarch', Color=(0.95, 0.55, 0.3, 1.0), MaxSteps=128.0, WriteDepth=True)
    s.link('moveField.Result', 'raymarch.SdfField')
    draws = ['raymarch.DrawCommand', floor(s, 'floor', 3, 6, height=-0.95),
             mesh_view(s, 'surfaceView', 'surface.Geometry', 2, 0, C['teal'], 'Surface', translation=(-2.6, 0.0, 0.0)),
             'drawPoints', 'drawBounds']
    render_rig(s, draws, 7, camera=(0.0, 1.6, 7.0), target=(0.0, 0.0, 0.0), light=(3.0, 6.0, 5.0), fog=40.0)
    s.section('1  Mesh', ['surface'], S['source'])
    s.section('2  Points and the sparse tree', ['points', 'drawPoints', 'nano', 'boundsShader', 'bounds', 'drawBounds'], S['bodies'])
    s.section('3  Raymarched 3D texture', ['texture', 'volumeField', 'moveField', 'raymarch'], S['world'])
    s.tour('points', '[VolumeToPoints] gives one point per active voxel, as a CPU list and as a GPU buffer.', 'InfoFor', 'MaxPoints')
    s.tour('bounds', 'The orange boxes are the nodes of the sparse tree: a VDB stores data only where the surface is.', 'InfoFor', 'Level')
    s.tour('texture', '[VolumeToTexture3D] bakes a dense texture; MaxResolution caps its size along the longest axis.', 'InfoFor', 'MaxResolution')
    s.tour('volumeField', '[VolumeField] turns the texture into a field, so it combines with every SDF operator.', 'InfoFor')
    s.tour('raymarch', 'The raymarch is drawn first and writes depth, so the meshes and points sit correctly around it.', 'InfoFor', 'WriteDepth')
    return s


def nano_vdb_render():
    s = Scene('NanoVdbRenderExample', 'Liquid, smoke and fire raymarched from NanoVDB grids in one glass box.',
              "[NanoVdbRender] raymarches three NanoVDB grids at once: a reflective liquid, smoke and fire. Each starts as a "
              "[VolumePrimitive] - a slab, a ball and a capsule - roughened once by [VolumeNoise] and uploaded to the GPU with "
              "[VolumeToNanoVdb]. The motion comes from the renderer itself: its procedural noise, driven by [Time], makes the "
              "smoke billow and the flames flicker without rebuilding a grid.\n\n"
              "AKA: smoke, fire, flames, liquid, water, nanovdb, gpu volume, raymarch, glass\n\n"
              "Tips\n" + TIPS_COMMON + "\n- All three grid inputs must be connected; LiquidAmount, SmokeDensity and FireIntensity switch them on and off.\n"
              "- StepCount trades quality against GPU time.\n"
              "- Animating a grid on the CPU (a moving noise Offset, say) rebuilds and uploads it every frame; prefer the "
              "renderer's Time-driven noise for motion.")
    grids = [('liquid', 'LiquidSlab', 1, (1.9, 0.62, 1.9), (0.0, -0.68, 0.0), 0.04, 3.0, (2.2, 2, 0.07, 3)),
             ('smoke', 'SmokeBall', 0, (1.0, 1.0, 1.0), (0.0, 0.8, 0.0), 0.05, 8.0, (1.6, 3, 0.28, 11)),
             ('fire', 'FlameCapsule', 4, (0.55, 1.0, 0.55), (0.0, -0.05, 0.0), 0.035, 6.0, (3.0, 3, 0.14, 7))]
    s.op('clock', 'Time', 0, 7, name='Clock', SpeedFactor=1.0)
    for index, (key, name, shape, size, center, voxel, band, (frequency, octaves, amplitude, seed)) in enumerate(grids):
        row = 1 + index * 2
        s.op(key, 'VolumePrimitive', 0, row, name=name, Shape=shape, Size=size, Center=center, VoxelSize=voxel, BandWidth=band)
        s.op(key + 'Noise', 'VolumeNoise', 1, row, name=name + 'Noise', Mode=2, Fractal=1, Octaves=octaves, Frequency=frequency,
             Amplitude=amplitude, Seed=seed)
        s.op(key + 'Nano', 'VolumeToNanoVdb', 2, row, name=name + 'ToGpu')
        s.link(key + '.Volume', key + 'Noise.Volume')
        s.link(key + 'Noise.Result', key + 'Nano.Volume')
    s.op('box', 'NanoVdbRender', 3, 3, name='GlassBox', StepCount=96.0, LiquidAmount=0.85, SmokeDensity=6.0, FireIntensity=2.0,
         FireSoftness=0.3)
    s.link('liquidNano.ShaderResourceView', 'box.Liquid')
    s.link('smokeNano.ShaderResourceView', 'box.Smoke')
    s.link('fireNano.ShaderResourceView', 'box.Fire')
    s.link('clock.Timefloat', 'box.Time')
    draws = [floor(s, 'floor', 3, 6, height=-1.01), 'box.DrawCommand']
    render_rig(s, draws, 6, camera=(2.4, 1.4, 3.6), target=(0.0, 0.0, 0.0), light=(2.0, 5.0, 3.0), fog=30.0)
    s.section('1  Three shapes, roughened', [k for g in grids for k in (g[0], g[0] + 'Noise')], S['source'])
    s.section('2  Uploaded and raymarched', [g[0] + 'Nano' for g in grids] + ['box', 'clock'], S['world'])
    s.tour('liquid', 'Each element starts as a simple [VolumePrimitive]: a slab of liquid, a ball of smoke, a capsule of fire.', 'Info')
    s.tour('smokeNoise', 'Displacing with noise turns a ball into a billow. It is computed once, so it costs nothing per frame.', 'InfoFor', 'Amplitude')
    s.tour('smokeNano', '[VolumeToNanoVdb] flattens the grid into the GPU layout the renderer reads.', 'InfoFor')
    s.tour('box', 'Fade the three elements in and out with LiquidAmount, SmokeDensity and FireIntensity.', 'CallToAction', 'SmokeDensity')
    s.tour('box', 'StepCount is the raymarch quality.', 'Tip', 'StepCount')
    s.tour('clock', 'The renderer animates its own noise with Time, so the smoke and fire move although the grids stay put.', 'InfoFor')
    return s


def load_vdb():
    s = Scene('LoadVdbExample', 'Volumes from files: the Stanford bunny as a mesh and a smoke plume raymarched as density.',
              "[LoadVdb] reads OpenVDB files as written by Houdini, Blender or TiXL itself. The bunny is a level set: made coarser "
              "with [VolumeResample], meshed and scaled down. The plume is a fog volume: [VolumeToNanoVdb] uploads it and "
              "[VdbDensityField] makes its density a field for [RaymarchField]. [VolumeInfo] reports what a file contains.\n\n"
              "AKA: vdb, openvdb, houdini, import, file, bunny, smoke, cloud\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Files are watched and reload when they change on disk.\n"
              "- A file with several grids: pick one with GridName.")
    s.op('bunny', 'LoadVdb', 0, 1, name='Bunny', Path='Examples:volumes/bunny.vdb')
    s.op('coarse', 'VolumeResample', 1, 1, name='Coarser', VoxelSize=0.4)
    s.op('bunnySurface', 'VolumeToMesh', 2, 1, name='BunnySurface')
    s.link('bunny.Volume', 'coarse.Volume')
    s.op('info', 'VolumeInfo', 1, 2, name='BunnyInfo', LogToConsole=False)
    s.link('bunny.Volume', 'info.Volume')
    s.leaf('info')
    s.link('coarse.Result', 'bunnySurface.Volume')
    s.op('smoke', 'LoadVdb', 0, 4, name='Plume', Path='Examples:volumes/smoke.vdb')
    s.op('smokeNano', 'VolumeToNanoVdb', 1, 4, name='PlumeToGpu')
    s.link('smoke.Volume', 'smokeNano.Volume')
    s.op('density', 'VdbDensityField', 2, 4, name='Density', IsoValue=0.1, ValueScale=5.0, OutsideValue=1.5)
    s.link('smokeNano.Buffer', 'density.Grid')
    s.link('smokeNano.WorldMin', 'density.BoundsMin')
    s.link('smokeNano.WorldMax', 'density.BoundsMax')
    # The plume is about 50 x 105 x 50 units; scaled down it stands next to the bunny on the floor.
    s.op('placeSmoke', 'TransformField', 3, 4, name='ScaleAndPlace', UniformScale=0.035, Translation=(2.6, 0.43, 0.0))
    s.link('density.Result', 'placeSmoke.InputField')
    s.op('raymarch', 'RaymarchField', 4, 4, name='Raymarch', Color=(0.85, 0.86, 0.9, 1.0), MaxSteps=128.0, WriteDepth=True)
    s.link('placeSmoke.Result', 'raymarch.SdfField')
    draws = ['raymarch.DrawCommand', floor(s, 'floor', 3, 0, height=0.0),
             mesh_view(s, 'bunnyView', 'bunnySurface.Geometry', 3, 1, C['white'], 'Bunny', translation=(-1.2, 0.0, 0.0), scale=0.09)]
    render_rig(s, draws, 7, camera=(0.6, 2.6, 7.5), target=(0.6, 1.4, 0.0), light=(3.0, 7.0, 5.0), fog=40.0)
    s.section('1  A level set from a file', ['bunny', 'coarse', 'bunnySurface', 'info'], S['source'])
    s.section('2  A fog volume from a file', ['smoke', 'smokeNano', 'density', 'placeSmoke', 'raymarch'], S['world'])
    s.tour('bunny', '[LoadVdb] reads the first float grid of a .vdb file; vector grids come along.', 'InfoFor', 'Path')
    s.tour('coarse', 'Resampling to coarser voxels makes the mesh much lighter.', 'InfoFor', 'VoxelSize')
    s.tour('info', '[VolumeInfo] reports voxel count, memory and value range. Turn on LogToConsole to print it.', 'CallToAction', 'LogToConsole')
    s.tour('density', '[VdbDensityField] reads density from the GPU grid; IsoValue sets where the cloud is considered solid.', 'InfoFor', 'IsoValue')
    s.tour('placeSmoke', 'Fields are transformed like objects: this one is scaled down and moved next to the bunny.', 'InfoFor')
    return s


def vdb_to_sdf_field():
    s = Scene('VdbToSdfFieldExample', 'A volume inside the SDF world: a torus volume melts into a wandering SDF sphere.',
              "[VdbToSdfField] makes a volume's signed distances a field, so it mixes with every SDF operator. Here a torus volume "
              "is blended smoothly with an animated [SphereSDF] by [CombineSDF], placed on an SDF floor and raymarched. Nothing "
              "is meshed.\n\n"
              "AKA: sdf, signed distance field, raymarch, blend, metaball, smooth union\n\n"
              "Tips\n" + TIPS_COMMON + "\n- K is the blend radius of the smooth union; 0 gives a hard edge.\n"
              "- Any volume works, also one loaded with [LoadVdb].")
    s.op('torus', 'VolumePrimitive', 0, 1, name='TorusVolume', Shape=3, Size=(2.4, 0.7, 1.0), VoxelSize=0.04, BandWidth=4.0)
    s.op('nano', 'VolumeToNanoVdb', 1, 1, name='ToGpu')
    s.link('torus.Volume', 'nano.Volume')
    s.op('torusField', 'VdbToSdfField', 2, 1, name='TorusField', OutsideValue=10.0)
    s.link('nano.Buffer', 'torusField.Grid')
    s.link('nano.WorldMin', 'torusField.BoundsMin')
    s.link('nano.WorldMax', 'torusField.BoundsMax')
    s.op('wander', 'AnimVec3', 1, 3, name='Wander', Rates=(0.15, 0.22, 0.1), Amplitudes=(1.6, 0.5, 0.6))
    s.op('ball', 'SphereSDF', 2, 3, name='Ball', Radius=0.55)
    s.link('wander.Result', 'ball.Center')
    s.op('blend', 'CombineSDF', 3, 2, name='SmoothBlend', CombineMethod=4, K=0.45)
    s.link('torusField.Result', 'blend.InputFields')
    s.link('ball.Result', 'blend.InputFields')
    s.op('ground', 'PlaneSDF', 2, 5, name='Floor', Center=(0.0, -1.0, 0.0), Axis=1)
    s.op('scene', 'CombineSDF', 4, 3, name='WithFloor', CombineMethod=0)
    s.link('blend.Result', 'scene.InputFields')
    s.link('ground.Result', 'scene.InputFields')
    s.op('raymarch', 'RaymarchField', 5, 3, name='Raymarch', Color=(0.9, 0.6, 0.35, 1.0), AmbientOcclusion=(0.0, 0.0, 0.0, 0.7),
         AoDistance=0.8, MaxSteps=128.0, StepSize=0.5)
    s.link('scene.Result', 'raymarch.SdfField')
    # The scene key is taken by the SDF combine, so the rig is built under its own names.
    render_rig(s, ['raymarch.DrawCommand'], 7, camera=(0.0, 1.4, 5.5), target=(0.0, -0.1, 0.0), light=(3.0, 6.0, 4.0), fog=40.0, prefix='rig')
    s.section('1  The volume as a field', ['torus', 'nano', 'torusField'], S['source'])
    s.section('2  Blended with SDF shapes', ['wander', 'ball', 'blend', 'ground', 'scene', 'raymarch'], S['world'])
    s.tour('torusField', '[VdbToSdfField] reads the uploaded grid as a signed distance field.', 'InfoFor')
    s.tour('blend', 'UnionSmooth melts the two shapes together within K.', 'InfoFor', 'K')
    s.tour('raymarch', '[RaymarchField] draws the combined field directly; no mesh is built.', 'InfoFor')
    s.tour('blend', 'Set CombineMethod to CutOut to carve the ball out of the torus instead.', 'CallToAction', 'CombineMethod')
    return s


def shaking_water():
    s = Scene('VolumeShakingWaterExample', 'Water sloshing in a shaking open box, built entirely from volume CSG and noise.',
              "The container is an open box: a box minus a slightly smaller box with [VolumeCsg] Difference. The water is a slab "
              "displaced by [VolumeNoise] and clipped by Intersection to the inside of the box. Two [OscillateVec3] make the box "
              "shake and the water slosh.\n\n"
              "AKA: water, liquid, slosh, container, csg, noise, animation\n\n"
              "Tips\n" + TIPS_COMMON)
    s.op('outer', 'VolumePrimitive', 0, 1, name='OuterBox', Shape=1, Size=(3.0, 2.0, 3.0), VoxelSize=0.03, BandWidth=3.0)
    s.op('inner', 'VolumePrimitive', 0, 2, name='InnerBox', Shape=1, Size=(2.8, 2.0, 2.8), Center=(0.0, 0.15, 0.0), VoxelSize=0.03, BandWidth=3.0)
    s.op('openBox', 'VolumeCsg', 1, 1, name='OpenBox', Operation=2)
    s.link('outer.Volume', 'openBox.VolumeA')
    s.link('inner.Volume', 'openBox.VolumeB')
    s.op('boxSurface', 'VolumeToMesh', 2, 1, name='BoxSurface')
    s.link('openBox.Volume', 'boxSurface.Volume')
    s.op('water', 'VolumePrimitive', 0, 4, name='Water', Shape=1, Size=(2.6, 1.2, 2.6), Center=(0.0, -0.2, 0.0), VoxelSize=0.03, BandWidth=5.0)
    s.op('slosh', 'VolumeNoise', 1, 4, name='Waves', Mode=2, Fractal=1, Octaves=3, Frequency=2.2, Amplitude=3.0, Seed=11)
    s.link('water.Volume', 'slosh.Volume')
    s.op('clip', 'VolumePrimitive', 0, 5, name='ClipVolume', Shape=1, Size=(2.7, 2.0, 2.7), Center=(0.0, 0.15, 0.0), VoxelSize=0.03, BandWidth=3.0)
    s.op('clipped', 'VolumeCsg', 2, 4, name='ClipToBox', Operation=1)
    s.link('slosh.Result', 'clipped.VolumeA')
    s.link('clip.Volume', 'clipped.VolumeB')
    s.op('waterSurface', 'VolumeToMesh', 3, 4, name='WaterSurface')
    s.link('clipped.Volume', 'waterSurface.Volume')
    boxDraw = mesh_view(s, 'boxView', 'boxSurface.Geometry', 3, 1, (0.22, 0.24, 0.28, 1.0), 'Box')
    waterDraw = mesh_view(s, 'waterView', 'waterSurface.Geometry', 4, 4, (0.3, 0.62, 0.95, 1.0), 'Water')
    s.op('shake', 'OscillateVec3', 6, 0, name='BoxShake', Amplitude=(0.08, 0.02, 0.06), AmplitudeScale=1.0, Period=(0.9, 1.4, 1.1),
         Phase=(0.0, 0.8, 1.7), SpeedFactor=1.0)
    s.op('sloshMove', 'OscillateVec3', 7, 3, name='WaterSlosh', Amplitude=(0.06, 0.0, 0.09), AmplitudeScale=1.0, Period=(1.25, 1.0, 1.45),
         Phase=(0.6, 0.0, 1.1), SpeedFactor=1.0)
    s.op('boxGroup', 'Group', 7, 1, name='ShakeBox')
    s.op('waterGroup', 'Group', 8, 4, name='SloshWater')
    s.link(boxDraw + '.Output', 'boxGroup.Commands')
    s.link('shake.Result', 'boxGroup.Translation')
    s.link(waterDraw + '.Output', 'waterGroup.Commands')
    s.link('sloshMove.Result', 'waterGroup.Translation')
    draws = [floor(s, 'floor', 7, 6, height=-1.02), 'boxGroup', 'waterGroup']
    render_rig(s, draws, 10, camera=(3.6, 3.4, 5.5), target=(0.0, -0.2, 0.0), light=(3.0, 7.0, 4.0), fog=40.0)
    s.section('1  An open box from CSG', ['outer', 'inner', 'openBox', 'boxSurface'], S['source'])
    s.section('2  Water: a noisy slab clipped to the box', ['water', 'slosh', 'clip', 'clipped', 'waterSurface'], S['bodies'])
    s.section('3  Shake and slosh', ['shake', 'sloshMove', 'boxGroup', 'waterGroup'], S['world'])
    s.tour('openBox', 'Difference carves the inner box out of the outer one, leaving walls and a floor.', 'InfoFor', 'Operation')
    s.tour('slosh', 'Displacing the water slab with noise gives it waves.', 'InfoFor', 'Amplitude')
    s.tour('clipped', 'Intersection keeps only the water inside the box, so no wave pokes through a wall.', 'InfoFor', 'Operation')
    s.tour('shake', '[OscillateVec3] moves the whole box a little: the meshes stay, only their [Group] moves.', 'InfoFor')
    return s


SCENES = [volume_primitive, volume_csg, volume_noise, volume_from_mesh, volume_outputs, nano_vdb_render, load_vdb, vdb_to_sdf_field,
          shaking_water]

if __name__ == '__main__':
    root = sys.argv[1]
    gen.load_operators(root)
    for make in SCENES:
        scene = make()
        scene.write(root)
        print(f'{scene.name}: {len(scene.ops)} ops, {len(scene.links)} links, {len(scene.tours)} tour points')
