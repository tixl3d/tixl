"""Generates VdbFilesExample in Operators/Examples/Symbols/Lib/volume from a local folder of .vdb files.

The example reads files outside the repository, so it is a local test scene and not part of the shipped examples.
Run from anywhere:  python3 Scripts/example-scenes/vdb_files.py <repository root> [vdb folder]
The editor must not have the example loaded while it is rewritten, or its next save overwrites it.
"""
import sys
import gen
from gen import PALETTE as C, SECTION_COLORS as S, ground, draw_mesh_at_bodies, mesh_view, floor, render_rig

DEFAULT_FOLDER = 'D:\\DEVDEVDEV\\vdbs'

# The OpenVDB sample models, each placed on load so it stands on the floor about two metres tall:
# (key, label, file, scale, translation, meshing voxel size in scene units, color)
SCULPTURES = [
    ('armadillo', 'Armadillo', 'armadillo.vdb', 0.0158, (-3.0, 0.862, 0.0), 0.019, C['white']),
    ('buddha', 'Buddha', 'buddha.vdb', 0.0336, (-0.6, 0.013, -0.035), 0.015, (0.86, 0.72, 0.42, 1.0)),
    ('knot', 'Knot', 'torus_knot_helix.vdb', 5.2, (-0.25, -0.84, -1.82), 0.0078, C['teal']),
]


def vdb_files(folder):
    s = gen.Scene('VdbFilesExample', 'OpenVDB sample models and an animated EmberGen fire, loaded from a folder of .vdb files.',
                  "A test scene for real-world VDB files. Three level sets from the OpenVDB samples - the armadillo, the buddha and "
                  "a torus knot - are read with [LoadVdb], made coarser with [VolumeResample] and meshed with [VolumeToMesh]. "
                  "Balls fall onto the armadillo, whose mesh is their collider.\n\n"
                  "The fire is an EmberGen cache of 100 frames, played as a sequence: Path marks the frame number with %d and "
                  "Frame follows the playback time. EmberGen writes Z-up files in voxel units, so [LoadVdb] turns them upright "
                  "and scales them as it reads them. Its temperature and flames grids are negated with [VolumeMath], because "
                  "[NanoVdbRender] reads density from the depth below the zero level, and raymarched together.\n\n"
                  "Every file is read and processed on background threads: the scene appears piece by piece while playback "
                  "keeps running.\n\n"
                  f"The files are expected in {folder}; change the Path of each [LoadVdb] for another folder.\n\n"
                  "AKA: vdb files, openvdb samples, embergen, fire sequence, armadillo, buddha\n\n"
                  "Tips\n"
                  "- The armadillo file holds 22 million voxels: it takes a few seconds to appear, the rest keeps playing.\n"
                  "- A finer VoxelSize on [VolumeResample] gives a smoother mesh with more triangles.\n"
                  "- The ground explosion cache plays the same way: point Path at ground_explosion_####.vdb and Mod at 200.",
                  folder='volume')

    # Sculptures, meshed; the armadillo also collides.
    for index, (key, label, file, scale, translation, voxel, color) in enumerate(SCULPTURES):
        row = 1 + index * 2
        s.op(key, 'LoadVdb', 0, row, name=label, Path=f'{folder}\\{file}', Scale=scale, Translation=translation)
        s.op(key + 'Coarse', 'VolumeResample', 1, row, name=label + 'Coarser', VoxelSize=voxel)
        s.op(key + 'Surface', 'VolumeToMesh', 2, row, name=label + 'Surface')
        s.link(key + '.Volume', key + 'Coarse.Volume')
        s.link(key + 'Coarse.Result', key + 'Surface.Volume')
    sculptureDraws = [mesh_view(s, key + 'View', key + 'Surface.Geometry', 3, 1 + index * 2, color, label)
                      for index, (key, label, *_rest, color) in enumerate(SCULPTURES)]

    s.op('statue', 'RigidBody', 6, 7, name='ArmadilloCollider', Kind=1, Friction=0.5)
    s.link('armadilloSurface.Geometry', 'statue.Geometry')
    ground(s, 'ground', 6, 8, top=0.0)
    s.op('ballPoints', 'ScatterPointsInVolume', 4, 9, name='BallPoints', Count=160, Center=(-3.0, 3.6, 0.0), Size=(1.6, 1.2, 1.2), Seed=5)
    s.op('balls', 'RigidBody', 6, 9, name='Balls', Shape=1, Radius=0.06, Bounciness=0.3)
    s.link('ballPoints.ResultList', 'balls.Points')
    s.op('world', 'PhysicsWorld', 8, 8, name='World')
    for body in ['ground', 'statue', 'balls']:
        s.link(body + '.OutBody', 'world.Bodies')
    s.op('ballMesh', 'SphereMesh', 8, 10, name='BallMesh', Radius=0.5, Segments=(12, 8))
    ballsDraw = draw_mesh_at_bodies(s, 'ballsView', 'world', 'balls', 'ballMesh.Data', 9, 9, C['orange'], 'Balls', apply_scale=True)

    # The fire sequence: playback time -> frame -> two grids of each frame -> GPU -> raymarch.
    s.op('clock', 'Time', 0, 12, name='Clock', Mode=2, Units=1)
    s.op('fps', 'Multiply', 1, 12, name='FramesPerSecond', B=24.0)
    s.op('frameIndex', 'FloatToInt', 2, 12, name='FrameIndex')
    s.op('loop', 'ModInt', 3, 12, name='Loop', Mod=100)
    s.link('clock.Timefloat', 'fps.A')
    s.link('fps.Result', 'frameIndex.FloatValue')
    s.link('frameIndex.Integer', 'loop.Value')
    pattern = f'{folder}\\Fire_01\\Fire_01\\embergen_fire_a_%d.vdb'
    for key, label, grid, factor, row in [('heat', 'Temperature', 'temperature', -0.8, 13), ('flames', 'Flames', 'flames', -6.0, 15)]:
        s.op(key, 'LoadVdb', 4, row, name=label, Path=pattern, GridName=grid, Scale=0.023, ZUp=True, Translation=(4.05, 2.97, -0.55))
        s.op(key + 'Depth', 'VolumeMath', 5, row, name=label + 'AsDepth', Operation=2, UseConstant=True, Constant=factor)
        s.op(key + 'Nano', 'VolumeToNanoVdb', 6, row, name=label + 'ToGpu')
        s.link('loop.Result', key + '.Frame')
        s.link(key + '.Volume', key + 'Depth.VolumeA')
        s.link(key + 'Depth.Result', key + 'Nano.Volume')
    s.op('fire', 'NanoVdbRender', 7, 14, name='Fire', StepCount=128.0, LiquidAmount=0.0, SmokeDensity=3.0, SmokeColor=(0.32, 0.26, 0.22, 1.0),
         SmokeSoftness=0.6, FireIntensity=2.5, FireSoftness=0.4, NoiseAmount=0.3, EdgeColor=(0.6, 0.7, 0.8, 0.0))
    s.link('heatNano.ShaderResourceView', 'fire.Liquid')
    s.link('heatNano.ShaderResourceView', 'fire.Smoke')
    s.link('flamesNano.ShaderResourceView', 'fire.Fire')
    s.link('heatNano.WorldMin', 'fire.BoxMin')
    s.link('heatNano.WorldMax', 'fire.BoxMax')
    s.link('clock.Timefloat', 'fire.Time')

    draws = [floor(s, 'floor', 9, 1, height=0.0, size=40.0)] + sculptureDraws + [ballsDraw, 'fire.DrawCommand']
    render_rig(s, draws, 12, camera=(0.6, 2.8, 9.5), target=(0.6, 1.1, 0.0), light=(3.0, 8.0, 6.0), fog=50.0)

    s.section('1  Level sets from files, meshed', [k for sculpture in SCULPTURES for k in (sculpture[0], sculpture[0] + 'Coarse', sculpture[0] + 'Surface')],
              S['source'])
    s.section('2  Balls on the armadillo', ['statue', 'ground', 'ballPoints', 'balls', 'world', 'ballMesh'], S['bodies'])
    s.section('3  An EmberGen fire sequence', ['clock', 'fps', 'frameIndex', 'loop', 'heat', 'heatDepth', 'heatNano', 'flames', 'flamesDepth',
                                              'flamesNano', 'fire'], S['world'])
    s.tour('armadillo', '[LoadVdb] reads the file on a background thread and places it: Scale and Translation bring it to scene size.', 'InfoFor', 'Scale')
    s.tour('armadilloCoarse', 'The file has 22 million voxels; resampling to coarser voxels keeps the mesh light.', 'InfoFor', 'VoxelSize')
    s.tour('statue', 'The armadillo mesh is a static collider; its collision tree is built in the background too.', 'InfoFor')
    s.tour('heat', 'A sequence: %d in Path is replaced by Frame. ZUp stands the EmberGen cache upright.', 'InfoFor', 'ZUp')
    s.tour('loop', 'Playback time at 24 frames per second, looped over the 100 frames of the cache.', 'InfoFor', 'Mod')
    s.tour('heatDepth', '[NanoVdbRender] reads density from the depth below zero, so the positive temperature is negated.', 'InfoFor', 'Constant')
    s.tour('fire', 'Temperature is drawn as hot smoke, flames as fire. Tune SmokeDensity and FireIntensity to taste.', 'CallToAction', 'FireIntensity')
    return s


if __name__ == '__main__':
    root = sys.argv[1]
    folder = sys.argv[2] if len(sys.argv) > 2 else DEFAULT_FOLDER
    gen.load_operators(root)
    scene = vdb_files(folder)
    scene.write(root)
    print(f'{scene.name}: {len(scene.ops)} ops, {len(scene.links)} links, {len(scene.tours)} tour points')
