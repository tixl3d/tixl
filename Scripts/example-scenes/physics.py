"""Generates the physics example graphs in Operators/Examples/Symbols/Lib/physics.

Run from anywhere:  python3 Scripts/example-scenes/physics.py <repository root>
The editor must not have the examples loaded while they are rewritten, or its next save overwrites them.
"""
import sys
import gen
from gen import Scene, PALETTE as C, SECTION_COLORS as S, ground, draw_bodies, draw_mesh_at_bodies, cpu_points, render_rig

TIPS_COMMON = "- Playback must run for the simulation to advance; press Reset on the World to start over."


def physics_world():
    s = Scene('PhysicsWorldExample', 'Physics basics: bodies, materials, a domino run and contact sparks in one world.',
              "The basics of the physics package: a [PhysicsWorld] simulates the bodies that [RigidBody] operators describe.\n\n"
              "Five balls show Bounciness from 0 to 1, a ball tips over a row of dominoes made from points, and capsules and cylinders "
              "tumble onto the floor. Every impact is reported by the world's Contacts output and drawn as a spark. "
              "[RigidBodyGeometry] hands the simulated bodies back as geometry, so each one is drawn as exactly the shape it collides as.\n\n"
              "AKA: physics, rigid body, simulation, bepu, bounce, domino, collision\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Select a ball and change Bounciness or Friction while it plays.\n"
              "- A body without Geometry is a primitive; Shape picks box, sphere, capsule or cylinder.")
    ground(s, 'ground', 2, 0)
    balls = []
    for index in range(5):
        bounce = index / 4
        balls.append(s.op(f'ball{index}', 'RigidBody', 2, 1 + index, name=f'Ball{int(bounce * 100)}', Shape=1, Radius=0.4,
                          Position=(-4.0 + index * 2.0, 5.0 + index * 0.01, -3.0), Bounciness=bounce, Friction=0.4))
    dominoes = cpu_points(s, 'dominoGrid', 'GridPoints', 0, 6, 'DominoRow', CountX=15, CountY=1, CountZ=1, Size=(0.6, 1.0, 1.0),
                          Scale=1.0, Center=(0.0, 0.5, 1.5))
    s.op('dominoes', 'RigidBody', 2, 6, name='Dominoes', Shape=0, Size=(0.15, 1.0, 0.6), Friction=0.5)
    s.link(dominoes, 'dominoes.Points')
    s.op('pusher', 'RigidBody', 2, 7, name='Pusher', Shape=1, Radius=0.3, Position=(-6.5, 1.5, 1.5), LinearVelocity=(5.0, 0.8, 0.0))
    s.op('rainPoints', 'ScatterPointsInVolume', 0, 8, name='RainPoints', Count=30, Center=(0.0, 7.0, 5.0), Size=(8.0, 4.0, 3.0), Seed=3)
    s.op('rainPoints2', 'ScatterPointsInVolume', 0, 9, name='RainPoints2', Count=30, Center=(0.0, 9.0, 5.0), Size=(8.0, 4.0, 3.0), Seed=8)
    s.op('capsules', 'RigidBody', 2, 8, name='Capsules', Shape=2, Radius=0.18, Length=0.6, Rotation=(0.0, 0.0, 70.0))
    s.op('cylinders', 'RigidBody', 2, 9, name='Cylinders', Shape=3, Radius=0.25, Length=0.35, Rotation=(40.0, 0.0, 0.0))
    s.link('rainPoints.ResultList', 'capsules.Points')
    s.link('rainPoints2.ResultList', 'cylinders.Points')

    bodies = ['ground'] + balls + ['dominoes', 'pusher', 'capsules', 'cylinders']
    s.op('world', 'PhysicsWorld', 4, 4, name='World', MaxContacts=128, ContactSpeed=1.5)
    for body in bodies:
        s.link(body + '.OutBody', 'world.Bodies')

    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 6, 0, C['ground'], 'Ground'),
             draw_bodies(s, 'ballsView', 'world', balls + ['pusher'], 6, 1, C['orange'], 'Balls'),
             draw_bodies(s, 'dominoView', 'world', 'dominoes', 6, 2, C['white'], 'Dominoes'),
             draw_bodies(s, 'shapesView', 'world', ['capsules', 'cylinders'], 6, 3, C['teal'], 'Shapes')]
    s.op('sparkMesh', 'SphereMesh', 6, 5, name='SparkMesh', Radius=0.12, Segments=(12, 6))
    s.op('sparks', 'DrawMeshAtPoints', 8, 5, name='DrawSparks', Color=(4.0, 2.4, 0.8, 1.0), UsePointScale=True)
    s.link('world.Contacts', 'sparks.GPoints')
    s.link('sparkMesh.Data', 'sparks.Mesh')
    draws.append('sparks')
    render_rig(s, draws, 10, camera=(0.0, 6.5, 14.0), target=(0.0, 1.2, 1.0))

    s.section('1  Bodies: what exists and how it behaves', bodies + ['dominoGrid', 'dominoGridCpu', 'rainPoints', 'rainPoints2'], S['bodies'])
    s.section('2  World: one simulation for all of them', ['world'], S['world'])
    s.section('3  Drawing: bodies back as geometry', [k for k in s.ops if k.endswith(('View' + 'Geometry', 'Mesh', 'Draw'))
                                                       and not k.startswith(('scene', 'spark'))] + ['sparkMesh', 'sparks'], S['draw'])
    s.tour('ground', 'Every body is a [RigidBody] description. This one is Static: it never moves, but everything collides with it.', 'Info')
    s.tour('ball4', 'Five balls differ only in Bounciness, from 0 to 1. Watch how high each one comes back.', 'InfoFor', 'Bounciness')
    s.tour('dominoes', 'Connecting Points makes one body per point: fifteen dominoes from one operator.', 'InfoFor', 'Points')
    s.tour('pusher', 'A starting LinearVelocity sends this ball into the first domino.', 'InfoFor', 'LinearVelocity')
    s.tour('world', 'The [PhysicsWorld] collects all bodies and steps the simulation with the playback time.', 'InfoFor')
    s.tour('world', 'Its Contacts output lists every impact of the frame as points.', 'InfoFor', 'MaxContacts')
    s.tour('sparks', 'Drawing a small mesh at the contact points gives a spark wherever something hits.', 'InfoFor')
    s.tour('ballsView' + 'Geometry', '[RigidBodyGeometry] returns bodies at their simulated poses as geometry, so no separate mesh has to match the collider.', 'InfoFor')
    s.tour('world', 'Press Reset on the World and play again.', 'CallToAction', 'Reset')
    return s


def rigid_body():
    s = Scene('RigidBodyExample', 'Bodies made from geometry: concave rings and letters that collide as what they are.',
              "[RigidBody] turns any geometry into a collider. Shape Auto splits concave geometry into several convex hulls, so the "
              "rings keep their holes and slide down the posts they land over, and the letters keep their counters.\n\n"
              "The same word falls twice: on the left with Pieces EachPartIsABody, so every letter is a body of its own, and on the right "
              "with WholeObject, so the word stays rigid. Bodies made from geometry weigh their volume times Density.\n\n"
              "AKA: convex decomposition, compound body, concave collider, mesh collider, multi-part, ring toss\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Set the Rings' Shape to ConvexHull and reset: every ring then fills its hole and rests on top of the posts.\n"
              "- Turn on ShowColliders on a [RigidBodyGeometry] to see the hulls.\n- MaxHulls trades accuracy against cost.")
    ground(s, 'ground', 3, 0)
    s.op('torus', 'VolumePrimitive', 0, 1, name='RingVolume', Shape=3, Size=(1.7, 0.36, 1.0), Center=(0.0, 0.0, 0.0), VoxelSize=0.04)
    s.op('torusGeometry', 'VolumeToMesh', 1, 1, name='RingGeometry', Isovalue=0.0)
    s.op('layFlat', 'TransformGeometry', 2, 1, name='LayFlat', Rotation=(90.0, 0.0, 0.0))
    s.link('torus.Volume', 'torusGeometry.Volume')
    s.link('torusGeometry.Geometry', 'layFlat.Geometry')
    s.op('ringPoints', 'ScatterPointsInVolume', 2, 2, name='RingPoints', Count=24, Center=(-3.0, 9.0, 0.0), Size=(4.0, 8.0, 4.0), Seed=3)
    s.op('rings', 'RigidBody', 3, 1, name='Rings', Friction=0.4)
    s.link('layFlat.Result', 'rings.Geometry')
    s.link('ringPoints.ResultList', 'rings.Points')
    s.op('postPoints', 'ScatterPointsInVolume', 2, 3, name='PostPoints', Count=9, Center=(-3.0, 1.25, 0.0), Size=(4.0, 0.01, 4.0), Seed=11)
    s.op('posts', 'RigidBody', 3, 3, name='Posts', Shape=3, Kind=1, Radius=0.12, Length=2.5)
    s.link('postPoints.ResultList', 'posts.Points')

    s.op('text', 'TextToCurves', 0, 5, name='Word', Text='TiXL', Size=1.6)
    s.op('letters', 'CurvesToGeometry', 1, 5, name='Letters', Depth=0.4)
    s.link('text.Curves', 'letters.Curves')
    s.op('lettersApart', 'RigidBody', 3, 5, name='LettersApart', Position=(2.2, 4.0, -1.0), Rotation=(-60.0, 10.0, 0.0), Friction=0.5)
    s.op('lettersTogether', 'RigidBody', 3, 6, name='WordTogether', Pieces=1, Position=(2.4, 4.0, 2.0), Rotation=(-60.0, -10.0, 0.0), Friction=0.5)
    s.link('letters.Result', 'lettersApart.Geometry')
    s.link('letters.Result', 'lettersTogether.Geometry')

    bodies = ['ground', 'rings', 'posts', 'lettersApart', 'lettersTogether']
    s.op('world', 'PhysicsWorld', 5, 3, name='World')
    for body in bodies:
        s.link(body + '.OutBody', 'world.Bodies')
    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 7, 0, C['ground'], 'Ground'),
             draw_bodies(s, 'ringsView', 'world', 'rings', 7, 1, C['orange'], 'Rings'),
             draw_bodies(s, 'postsView', 'world', 'posts', 7, 2, C['static'], 'Posts'),
             draw_bodies(s, 'apartView', 'world', 'lettersApart', 7, 3, C['teal'], 'LettersApart'),
             draw_bodies(s, 'togetherView', 'world', 'lettersTogether', 7, 4, C['violet'], 'WordTogether')]
    render_rig(s, draws, 11, camera=(1.0, 7.0, 12.0), target=(0.0, 1.0, 0.0))

    s.section('1  Rings: one concave geometry at every point', ['torus', 'torusGeometry', 'layFlat', 'ringPoints', 'rings', 'postPoints', 'posts', 'ground'], S['source'])
    s.section('2  Letters: parts apart or together', ['text', 'letters', 'lettersApart', 'lettersTogether'], S['bodies'])
    s.section('3  Simulate and draw', ['world'] + [k for k in s.ops if k.endswith(('Geometry', 'Mesh', 'Draw')) and 'View' in k], S['draw'])
    s.tour('torus', 'A torus from a volume. Any geometry works: [VolumeToMesh], [CubeGeometry], text, loaded models.', 'Info')
    s.tour('layFlat', '[TransformGeometry] lays it flat. A RigidBody\'s Rotation would also turn the cloud of points it is placed at.', 'InfoFor')
    s.tour('rings', 'Shape Auto splits the concave ring into convex hulls, so its hole stays open.', 'InfoFor', 'Shape')
    s.tour('rings', 'MaxHulls caps how many hulls one part is split into.', 'InfoFor', 'MaxHulls')
    s.tour('posts', 'Static cylinders, one per point.', 'InfoFor')
    s.tour('lettersApart', 'With Pieces EachPartIsABody every letter falls on its own.', 'InfoFor', 'Pieces')
    s.tour('lettersTogether', 'With WholeObject the parts form one rigid body.', 'InfoFor', 'Pieces')
    s.tour('lettersTogether', 'Geometry bodies weigh their volume times Density; MassMode Fixed uses Mass instead.', 'Tip', 'MassMode')
    s.tour('ringsView' + 'Geometry', 'Turn on ShowColliders to see the hulls each ring was split into.', 'CallToAction', 'ShowColliders')
    return s


def rigid_joint():
    s = Scene('RigidJointExample', 'Joints: a hanging rope, a seesaw on a hinge and a net draped over a ball.',
              "[RigidJoint] links bodies. Three set-ups show its modes side by side.\n\n"
              "The rope is a line of points joined link to link by one joint in Chain mode, and pinned in place by its first link with a "
              "second joint in World mode. The seesaw is a plank on a Hinge around a kinematic pivot; a crate drops on one end and "
              "launches the other. The net joins every two knots that start close together, with Mode Nearby, and drapes over a ball.\n\n"
              "AKA: constraint, rope, chain, hinge, ball socket, cloth, net, seesaw\n\n"
              "Tips\n" + TIPS_COMMON + "\n- A static body cannot be a joint anchor; use a Kinematic one.\n"
              "- Higher Frequency makes joints stiffer; ropes and nets like more Substeps on the World.")
    ground(s, 'ground', 3, 0)
    rope = cpu_points(s, 'ropeLine', 'LinePoints', 0, 1, 'RopeLine', Count=24, Center=(-6.0, 7.0, 0.0), Direction=(1.0, 0.0, 0.0), Length=6.0)
    s.op('links', 'RigidBody', 3, 1, name='RopeLinks', Shape=1, Radius=0.13, CollisionGroup=1, LinearDamping=0.1)
    s.link(rope, 'links.Points')
    s.op('chain', 'RigidJoint', 4, 1, name='Chain', Mode=1, Frequency=60.0)
    s.op('pin', 'RigidJoint', 4, 2, name='PinFirstLink', Mode=3, InstanceIndex=0, Frequency=60.0)
    s.link('links.OutBody', 'chain.BodyA')
    s.link('links.OutBody', 'pin.BodyA')

    s.op('pivot', 'RigidBody', 3, 3, name='Pivot', Shape=0, Kind=2, Size=(0.1, 0.1, 0.1), Position=(0.07765, 0.58527, 0.0))
    s.op('plank', 'RigidBody', 3, 4, name='Plank', Shape=0, Size=(6.0, 0.2, 1.5), Position=(0.0, 0.87505, 0.0), Rotation=(0.0, 0.0, 15.0),
         Mass=3.0, Friction=0.6)
    s.op('hinge', 'RigidJoint', 4, 3, name='Hinge', JointType=1, LocalOffsetA=(0.0, 0.0, 0.0), LocalOffsetB=(0.0, -0.3, 0.0),
         HingeAxisA=(0.0, 0.0, 1.0), HingeAxisB=(0.0, 0.0, 1.0), Frequency=60.0)
    s.link('pivot.OutBody', 'hinge.BodyA')
    s.link('plank.OutBody', 'hinge.BodyB')
    s.op('launched', 'RigidBody', 3, 5, name='LaunchedCrate', Shape=0, Size=(0.5, 0.5, 0.5), Position=(-2.3, 0.88, 0.0), Mass=1.0, Friction=0.6)
    s.op('dropped', 'RigidBody', 3, 6, name='DroppedCrate', Shape=0, Size=(0.9, 0.9, 0.9), Position=(2.5, 5.0, 0.0), Mass=6.0, Friction=0.6)

    s.op('ball', 'RigidBody', 3, 7, name='Ball', Shape=1, Kind=1, Radius=1.6, Position=(7.0, 1.6, 0.0))
    net = cpu_points(s, 'netGrid', 'GridPoints', 0, 8, 'NetGrid', CountX=14, CountY=1, CountZ=14, Size=(0.32, 1.0, 0.32), Scale=1.0,
                     Center=(7.0, 4.5, 0.0))
    s.op('knots', 'RigidBody', 3, 8, name='NetKnots', Shape=1, Radius=0.1, Mass=0.2, CollisionGroup=1, LinearDamping=0.3)
    s.link(net, 'knots.Points')
    s.op('mesh', 'RigidJoint', 4, 8, name='NearbyLinks', Mode=2, LinkDistance=0.36, Frequency=40.0)
    s.link('knots.OutBody', 'mesh.BodyA')

    bodies = ['ground', 'links', 'pivot', 'plank', 'launched', 'dropped', 'ball', 'knots']
    s.op('world', 'PhysicsWorld', 6, 4, name='World', Substeps=4, SolverIterations=8)
    for body in bodies:
        s.link(body + '.OutBody', 'world.Bodies')
    for joint in ['chain', 'pin', 'hinge', 'mesh']:
        s.link(joint + '.OutJoint', 'world.Joints')
    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 8, 0, C['ground'], 'Ground'),
             draw_bodies(s, 'ropeView', 'world', 'links', 8, 1, C['yellow'], 'Rope'),
             draw_bodies(s, 'seesawView', 'world', ['plank', 'pivot'], 8, 2, C['teal'], 'Seesaw'),
             draw_bodies(s, 'cratesView', 'world', ['launched', 'dropped'], 8, 3, C['orange'], 'Crates'),
             draw_bodies(s, 'ballView', 'world', 'ball', 8, 4, C['static'], 'Ball'),
             draw_bodies(s, 'netView', 'world', 'knots', 8, 5, C['red'], 'Net')]
    render_rig(s, draws, 12, camera=(0.5, 6.0, 15.0), target=(0.5, 2.2, 0.0))

    s.section('1  Rope: Chain and World joints', ['ropeLine', 'ropeLineCpu', 'links', 'chain', 'pin'], S['source'])
    s.section('2  Seesaw: a Hinge between a kinematic pivot and a plank', ['pivot', 'plank', 'hinge', 'launched', 'dropped'], S['bodies'])
    s.section('3  Net: Nearby links over a ball', ['ball', 'netGrid', 'netGridCpu', 'knots', 'mesh'], S['world'])
    s.tour('links', 'Twenty-four links from a line of points. CollisionGroup 1 lets neighbouring links overlap without pushing each other away.', 'Info')
    s.tour('chain', 'Mode Chain joins point 0 to 1, 1 to 2 and so on: one operator for the whole rope.', 'InfoFor', 'Mode')
    s.tour('pin', 'Mode World holds a body where it starts; InstanceIndex 0 pins only the first link.', 'InfoFor', 'InstanceIndex')
    s.tour('pivot', 'The pivot is Kinematic: it has infinite mass and never moves unless its Position changes.', 'InfoFor', 'Kind')
    s.tour('hinge', 'A Hinge lets the plank turn around the Z axis only.', 'InfoFor', 'JointType')
    s.tour('dropped', 'A heavy crate drops on the right end and catapults the light one on the left.', 'InfoFor', 'Mass')
    s.tour('mesh', 'Mode Nearby links every two knots closer than LinkDistance: a net, or cloth.', 'InfoFor', 'LinkDistance')
    s.tour('world', 'Joints like more Substeps and SolverIterations than loose bodies.', 'Tip', 'Substeps')
    return s


def rigid_force():
    s = Scene('RigidForceExample', 'Forces: a whirlwind lifts 300 boxes, and an impulse blasts them apart on demand.',
              "[RigidForce] pushes the dynamic bodies of a world. Three continuous forces make a whirlwind: a Vortex swirls the boxes "
              "around the vertical axis, a Radial force keeps them near the centre and a Directional force lifts them against gravity. "
              "A fourth force is an impulse: it fires once each time its Trigger is pressed.\n\n"
              "AKA: wind, vortex, tornado, attractor, gravity well, explosion, impulse, force field\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Negative Strength turns a radial pull into a push.\n"
              "- ScaleByMass makes heavy bodies react less; Field samples a [VectorField], e.g. from [VolumeToField].")
    ground(s, 'ground', 2, 0)
    s.op('boxPoints', 'ScatterPointsInVolume', 0, 1, name='BoxPoints', Count=300, Center=(0.0, 1.0, 0.0), Size=(10.0, 1.5, 10.0), Seed=3)
    s.op('boxes', 'RigidBody', 2, 1, name='Boxes', Shape=0, Size=(0.3, 0.3, 0.3), LinearDamping=0.2)
    s.link('boxPoints.ResultList', 'boxes.Points')
    s.op('vortex', 'RigidForce', 2, 3, name='Swirl', ForceType=2, Direction=(0.0, 1.0, 0.0), Strength=10.0, Radius=9.0, Falloff=0.5)
    s.op('pull', 'RigidForce', 2, 4, name='PullIn', ForceType=1, Strength=5.0, Radius=12.0, Falloff=0.0)
    s.op('lift', 'RigidForce', 2, 5, name='Lift', ForceType=0, Direction=(0.0, 1.0, 0.0), Strength=6.0)
    s.op('blast', 'RigidForce', 2, 6, name='Blast', ForceType=1, Position=(0.0, 0.5, 0.0), Strength=-14.0, Radius=10.0, IsImpulse=True)
    s.op('world', 'PhysicsWorld', 4, 2, name='World')
    for body in ['ground', 'boxes']:
        s.link(body + '.OutBody', 'world.Bodies')
    for force in ['vortex', 'pull', 'lift', 'blast']:
        s.link(force + '.OutForce', 'world.Forces')
    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 6, 0, C['ground'], 'Ground'),
             draw_bodies(s, 'boxesView', 'world', 'boxes', 6, 1, C['orange'], 'Boxes')]
    render_rig(s, draws, 10, camera=(0.0, 9.0, 20.0), target=(0.0, 4.0, 0.0), fog=70.0)
    s.section('1  Bodies', ['ground', 'boxPoints', 'boxes'], S['bodies'])
    s.section('2  Forces', ['vortex', 'pull', 'lift', 'blast'], S['source'])
    s.tour('vortex', 'A Vortex swirls bodies around the axis through Position along Direction; Radius and Falloff limit its reach.', 'InfoFor', 'ForceType')
    s.tour('pull', 'A Radial force pulls towards Position, which keeps the whirlwind together.', 'InfoFor')
    s.tour('lift', 'A Directional force is a constant push, here upwards against gravity.', 'InfoFor')
    s.tour('blast', 'Press Trigger: this impulse fires once and blasts the boxes outwards.', 'CallToAction', 'Trigger')
    s.tour('world', 'Every force goes into the Forces input of the world.', 'InfoFor')
    return s


def rigid_body_geometry():
    s = Scene('RigidBodyGeometryExample', 'Fracture: a cube shatters, and the collision hulls are shown next to the drawn geometry.',
              "[VoronoiFracture] cuts a cube into cells; a [RigidBody] makes every cell a body, so the cube breaks apart when it lands. "
              "[RigidBodyGeometry] returns the pieces as geometry, ready for [GeometryToMesh] or any other geometry operator.\n\n"
              "The ring on the right is drawn with ShowColliders: instead of its own surface you see the convex hulls it collides as.\n\n"
              "AKA: fracture, shatter, break, debris, voronoi, collider view, debug\n\n"
              "Tips\n" + TIPS_COMMON + "\n- More seed points give smaller pieces; small pieces weigh less because mass follows volume.\n"
              "- Toggle ShowColliders on the Pieces geometry too: fracture cells are already convex, so collider and drawing match.")
    ground(s, 'ground', 3, 0)
    s.op('cube', 'CubeGeometry', 0, 1, name='Cube', Size=(2.0, 2.0, 2.0))
    s.op('seeds', 'ScatterPointsInVolume', 0, 2, name='Seeds', Count=40, Size=(2.0, 2.0, 2.0), Seed=5)
    s.op('fracture', 'VoronoiFracture', 1, 1, name='Fracture', Async=False)
    s.link('cube.Geometry', 'fracture.Geometry')
    s.link('seeds.ResultList', 'fracture.Points')
    s.op('pieces', 'RigidBody', 3, 1, name='Pieces', Position=(-2.0, 5.0, 0.0), Rotation=(25.0, 20.0, 10.0), Friction=0.6)
    s.link('fracture.Result', 'pieces.Geometry')
    s.op('torus', 'VolumePrimitive', 0, 4, name='RingVolume', Shape=3, Size=(2.4, 0.6, 1.0), VoxelSize=0.05)
    s.op('torusGeometry', 'VolumeToMesh', 1, 4, name='RingGeometry')
    s.link('torus.Volume', 'torusGeometry.Volume')
    s.op('ring', 'RigidBody', 3, 4, name='Ring', Position=(2.5, 3.0, 0.0), Rotation=(30.0, 0.0, 0.0))
    s.link('torusGeometry.Geometry', 'ring.Geometry')
    s.op('world', 'PhysicsWorld', 5, 2, name='World')
    for body in ['ground', 'pieces', 'ring']:
        s.link(body + '.OutBody', 'world.Bodies')
    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 7, 0, C['ground'], 'Ground'),
             draw_bodies(s, 'piecesView', 'world', 'pieces', 7, 1, C['red'], 'Pieces'),
             draw_bodies(s, 'ringView', 'world', 'ring', 7, 3, C['yellow'], 'RingColliders', show_colliders=True)]
    render_rig(s, draws, 11, camera=(0.0, 4.5, 10.0), target=(0.0, 1.0, 0.0))
    s.section('1  Fracture', ['cube', 'seeds', 'fracture', 'pieces'], S['source'])
    s.section('2  A concave ring', ['torus', 'torusGeometry', 'ring'], S['bodies'])
    s.section('3  Bodies back as geometry', ['piecesViewGeometry', 'piecesViewMesh', 'piecesViewDraw', 'ringViewGeometry', 'ringViewMesh', 'ringViewDraw'], S['draw'])
    s.tour('fracture', '[VoronoiFracture] cuts the cube into one part per seed point.', 'InfoFor')
    s.tour('pieces', 'Geometry with several parts: every part becomes its own body, placed where it sits in the cube.', 'InfoFor', 'Pieces')
    s.tour('piecesViewGeometry', 'Each part comes back moved by its body. Any geometry operator can follow, for example [BevelGeometry].', 'InfoFor')
    s.tour('ringViewGeometry', 'ShowColliders draws the convex hulls instead: the ring was split into several, which keep its hole open.', 'InfoFor', 'ShowColliders')
    return s


def rigid_body_points():
    s = Scene('RigidBodyPointsExample', 'A car on four motorised wheels drives into a wall of crates drawn as instanced meshes.',
              "The car is a chassis and four cylinder wheels from a 2 x 2 point grid. One Hinge joint with AutoAnchor fits all four "
              "wheels at once and its MotorSpeed drives them.\n\n"
              "[RigidBodyPoints] picks one body description's poses out of the world as a point buffer. That is the cheap way to draw "
              "many identical bodies: one mesh repeated at every point with [RepeatMeshAtPoints], instead of rebuilding geometry every frame.\n\n"
              "AKA: vehicle, car, wheels, motor, axle, instancing, crates\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Bodies with the same CollisionGroup pass through each other; the car parts share one so the wheels can touch the chassis.\n"
              "- Negative MotorSpeed drives backwards.")
    ground(s, 'ground', 3, 0)
    s.op('chassis', 'RigidBody', 3, 1, name='Chassis', Shape=0, Size=(2.0, 0.5, 4.0), Mass=10.0, Position=(0.0, 1.0, 0.0), CollisionGroup=1)
    wheels = cpu_points(s, 'wheelGrid', 'GridPoints', 0, 2, 'WheelGrid', CountX=2, CountY=1, CountZ=2, Size=(2.6, 1.0, 2.8), Scale=1.0,
                        Center=(0.0, 0.6, 0.0), OrientationAxis=(0.0, 0.0, 1.0), OrientationAngle=90.0)
    s.op('wheels', 'RigidBody', 3, 2, name='Wheels', Shape=3, Radius=0.5, Length=0.4, Mass=2.0, Friction=1.5, CollisionGroup=1)
    s.link(wheels, 'wheels.Points')
    s.op('axles', 'RigidJoint', 4, 2, name='Axles', JointType=1, AutoAnchor=True, HingeAxisB=(0.0, 1.0, 0.0), Frequency=60.0, MotorSpeed=8.0)
    s.link('chassis.OutBody', 'axles.BodyA')
    s.link('wheels.OutBody', 'axles.BodyB')
    crates = cpu_points(s, 'crateGrid', 'GridPoints', 0, 4, 'CrateGrid', CountX=6, CountY=5, CountZ=1, Size=(0.72, 0.7, 1.0), Scale=1.0,
                        Center=(0.0, 1.75, 12.0))
    s.op('crates', 'RigidBody', 3, 4, name='Crates', Shape=0, Size=(0.7, 0.7, 0.7), MassMode=2, Density=1.2)
    s.link(crates, 'crates.Points')
    s.op('world', 'PhysicsWorld', 5, 2, name='World', Substeps=4, SolverIterations=8)
    for body in ['ground', 'chassis', 'wheels', 'crates']:
        s.link(body + '.OutBody', 'world.Bodies')
    s.link('axles.OutJoint', 'world.Joints')
    s.op('crateMesh', 'CubeMesh', 7, 4, name='CrateMesh')
    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 7, 0, C['ground'], 'Ground'),
             draw_bodies(s, 'carView', 'world', ['chassis', 'wheels'], 7, 1, C['blue'], 'Car'),
             draw_mesh_at_bodies(s, 'crateView', 'world', 'crates', 'crateMesh.Data', 7, 3, C['yellow'], 'Crates', apply_scale=True)]
    render_rig(s, draws, 11, camera=(9.0, 5.0, -4.0), target=(0.0, 1.0, 6.0), fog=80.0)
    s.section('1  Car: chassis, wheels and one hinge for all four', ['chassis', 'wheelGrid', 'wheelGridCpu', 'wheels', 'axles'], S['source'])
    s.section('2  Crates', ['crateGrid', 'crateGridCpu', 'crates'], S['bodies'])
    s.section('3  Instanced drawing', ['crateMesh', 'crateViewPoints', 'crateViewMesh', 'crateViewDraw'], S['draw'])
    s.tour('wheels', 'Four wheels from a 2 x 2 grid. The grid turns every point 90 degrees, so the cylinder axis lies sideways.', 'InfoFor')
    s.tour('axles', 'AutoAnchor puts the hinge at each wheel, so one joint fits all four. MotorSpeed drives them.', 'InfoFor', 'MotorSpeed')
    s.tour('crates', 'MassMode FromDensity weighs the crates by their volume.', 'InfoFor', 'MassMode')
    s.tour('crateViewPoints', '[RigidBodyPoints] outputs just the crates\' poses, with each body\'s size as the point scale.', 'InfoFor')
    s.tour('crateViewMesh', 'One cube mesh repeated at every pose: cheap even for thousands of bodies.', 'InfoFor')
    return s


def mesh_to_geometry():
    s = Scene('MeshToGeometryExample', 'GPU meshes as physics bodies: tori, icosahedra and cylinders rain into a pile.',
              "[MeshToGeometry] reads a GPU mesh back as geometry, so meshes from [TorusMesh], [IcosahedronMesh], [LoadGltf] and every "
              "other mesh operator can collide. The readback only happens when the mesh changes.\n\n"
              "Each mesh becomes a body at every scattered point. They are drawn with their original mesh through [RigidBodyPoints] "
              "and [RepeatMeshAtPoints], so smooth normals and UVs stay as they were. The tori are concave and keep their holes.\n\n"
              "AKA: mesh collider, mesh readback, gltf physics, import, cpu mesh\n\n"
              "Tips\n" + TIPS_COMMON + "\n- A mesh with several chunks, such as a loaded model, arrives as several parts: use Pieces to choose between one body per part and one for the whole model.")
    ground(s, 'ground', 4, 0)
    s.op('bowl', 'VolumePrimitive', 0, 1, name='BowlBlock', Shape=1, Size=(9.0, 2.0, 9.0), Center=(0.0, 1.0, 0.0), VoxelSize=0.15)
    s.op('carver', 'VolumePrimitive', 0, 2, name='BowlCarver', Shape=0, Size=(8.0, 8.0, 8.0), Center=(0.0, 4.4, 0.0), VoxelSize=0.15)
    s.op('carve', 'VolumeCsg', 1, 1, name='Carve', Operation=2)
    s.op('bowlGeometry', 'VolumeToMesh', 2, 1, name='BowlGeometry')
    s.link('bowl.Volume', 'carve.VolumeA')
    s.link('carver.Volume', 'carve.VolumeB')
    s.link('carve.Volume', 'bowlGeometry.Volume')
    s.op('bowlBody', 'RigidBody', 4, 1, name='Bowl', Kind=1, Friction=0.6)
    s.link('bowlGeometry.Geometry', 'bowlBody.Geometry')

    meshes = [('torus', 'TorusMesh', 'Tori', {'Radius': 0.4, 'Thickness': 0.15, 'Segments': (32, 16)}, C['orange'], 3),
              ('ico', 'IcosahedronMesh', 'Icosahedra', {'Scale': 0.35}, C['teal'], 5),
              ('cylinder', 'CylinderMesh', 'Cylinders', {'Radius': 0.22, 'Height': 0.6}, C['violet'], 7)]
    bodies = ['ground', 'bowlBody']
    for index, (key, opname, label, values, _, seed) in enumerate(meshes):
        row = 3 + index * 2
        s.op(key, opname, 0, row, name=label + 'Mesh', **values)
        s.op(key + 'Geometry', 'MeshToGeometry', 2, row, name=label + 'Geometry')
        s.link(key + '.Data', key + 'Geometry.Mesh')
        s.op(key + 'Points', 'ScatterPointsInVolume', 2, row + 1, name=label + 'Points', Count=25, Center=(0.0, 7.0 + index * 2.5, 0.0),
             Size=(5.0, 2.0, 5.0), Seed=seed)
        s.op(key + 'Body', 'RigidBody', 4, row, name=label, Friction=0.5)
        s.link(key + 'Geometry.Result', key + 'Body.Geometry')
        s.link(key + 'Points.ResultList', key + 'Body.Points')
        bodies.append(key + 'Body')
    s.op('world', 'PhysicsWorld', 6, 3, name='World')
    for body in bodies:
        s.link(body + '.OutBody', 'world.Bodies')
    draws = [draw_bodies(s, 'groundView', 'world', ['ground', 'bowlBody'], 8, 0, C['static'], 'Ground')]
    for index, (key, _, label, _, color, _) in enumerate(meshes):
        draws.append(draw_mesh_at_bodies(s, key + 'View', 'world', key + 'Body', key + '.Data', 8, 2 + index * 2, color, label))
    render_rig(s, draws, 12, camera=(0.0, 9.0, 11.0), target=(0.0, 1.0, 0.0))
    s.section('1  A bowl carved from volumes', ['bowl', 'carver', 'carve', 'bowlGeometry', 'bowlBody', 'ground'], S['source'])
    s.section('2  GPU meshes become bodies', [k for m in meshes for k in (m[0], m[0] + 'Geometry', m[0] + 'Points', m[0] + 'Body')], S['bodies'])
    s.section('3  Drawn with their own meshes', [k for m in meshes for k in (m[0] + 'ViewPoints', m[0] + 'ViewMesh', m[0] + 'ViewDraw')], S['draw'])
    s.tour('torusGeometry', '[MeshToGeometry] reads the GPU mesh back. WeldPoints reconnects faces the mesh split for its normals.', 'InfoFor')
    s.tour('torusBody', 'With Points connected the whole mesh sits at every point. Shape Auto splits the concave torus into hulls.', 'InfoFor')
    s.tour('torusViewMesh', 'Drawing with the original mesh keeps its smooth normals: [RepeatMeshAtPoints] places it at every body.', 'InfoFor')
    s.tour('bowlBody', 'Static bodies made from geometry collide as their exact triangles.', 'InfoFor', 'Kind')
    return s


def volumes():
    s = Scene('PhysicsVolumesExample', 'Volumes as colliders: the Stanford bunny from a VDB file and a bowl carved with CSG.',
              "Any volume becomes a collider through [VolumeToMesh]. On the left, the bunny is loaded with [LoadVdb], made coarser with "
              "[VolumeResample] and scaled down; on the right, a bowl is carved out of a block with [VolumeCsg]. Balls rain on both.\n\n"
              "AKA: vdb, nanovdb, openvdb, level set, voxel collider, csg, bunny\n\n"
              "Tips\n" + TIPS_COMMON + "\n- A coarser VoxelSize gives fewer triangles and a cheaper collider.\n"
              "- A volume that changes rebuilds the collider, so animate it sparingly.")
    ground(s, 'ground', 4, 0)
    s.op('bunny', 'LoadVdb', 0, 1, name='Bunny', Path='Examples:volumes/bunny.vdb')
    s.op('coarse', 'VolumeResample', 1, 1, name='Coarser', VoxelSize=0.5)
    s.op('bunnySurface', 'VolumeToMesh', 2, 1, name='BunnySurface')
    s.op('bunnyPlace', 'TransformGeometry', 3, 1, name='ScaleAndPlace', UniformScale=0.12, Translation=(-4.5, 0.0, 0.0))
    s.link('bunny.Volume', 'coarse.Volume')
    s.link('coarse.Result', 'bunnySurface.Volume')
    s.link('bunnySurface.Geometry', 'bunnyPlace.Geometry')
    s.op('bunnyBody', 'RigidBody', 4, 1, name='BunnyCollider', Kind=1, Friction=0.5)
    s.link('bunnyPlace.Result', 'bunnyBody.Geometry')
    s.op('block', 'VolumePrimitive', 0, 3, name='Block', Shape=1, Size=(6.0, 2.2, 6.0), Center=(4.5, 1.1, 0.0), VoxelSize=0.12)
    s.op('carver', 'VolumePrimitive', 0, 4, name='Carver', Shape=0, Size=(5.4, 5.4, 5.4), Center=(4.5, 3.6, 0.0), VoxelSize=0.12)
    s.op('carve', 'VolumeCsg', 1, 3, name='Carve', Operation=2)
    s.op('bowlSurface', 'VolumeToMesh', 2, 3, name='BowlSurface')
    s.link('block.Volume', 'carve.VolumeA')
    s.link('carver.Volume', 'carve.VolumeB')
    s.link('carve.Volume', 'bowlSurface.Volume')
    s.op('bowlBody', 'RigidBody', 4, 3, name='BowlCollider', Kind=1, Friction=0.5)
    s.link('bowlSurface.Geometry', 'bowlBody.Geometry')
    s.op('ballPoints', 'ScatterPointsInVolume', 2, 5, name='BallPoints', Count=260, Center=(0.0, 9.0, 0.0), Size=(14.0, 6.0, 5.0), Seed=9)
    s.op('balls', 'RigidBody', 4, 5, name='Balls', Shape=1, Radius=0.2, LinearDamping=0.12, AngularDamping=0.25)
    s.link('ballPoints.ResultList', 'balls.Points')
    s.op('world', 'PhysicsWorld', 6, 2, name='World')
    for body in ['ground', 'bunnyBody', 'bowlBody', 'balls']:
        s.link(body + '.OutBody', 'world.Bodies')
    s.op('ballMesh', 'SphereMesh', 7, 2, name='BallMesh', Radius=0.5, Segments=(12, 8))
    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 8, 0, C['ground'], 'Ground'),
             draw_bodies(s, 'collidersView', 'world', ['bunnyBody', 'bowlBody'], 8, 1, C['white'], 'Colliders'),
             draw_mesh_at_bodies(s, 'ballsView', 'world', 'balls', 'ballMesh.Data', 8, 2, C['blue'], 'Balls', apply_scale=True)]
    render_rig(s, draws, 12, camera=(0.0, 8.0, 14.0), target=(0.0, 1.5, 0.0))
    s.section('1  A VDB file', ['bunny', 'coarse', 'bunnySurface', 'bunnyPlace', 'bunnyBody'], S['source'])
    s.section('2  Carved with CSG', ['block', 'carver', 'carve', 'bowlSurface', 'bowlBody'], S['bodies'])
    s.tour('bunny', '[LoadVdb] loads a level set from a .vdb file.', 'InfoFor')
    s.tour('coarse', 'A coarser grid gives fewer triangles: a cheaper collider that looks the same from a distance.', 'InfoFor', 'VoxelSize')
    s.tour('bunnyBody', 'A Static body with Geometry collides as the exact mesh.', 'InfoFor')
    s.tour('carve', '[VolumeCsg] subtracts the sphere from the block: a bowl that catches the balls.', 'InfoFor', 'Operation')
    s.tour('ballsViewPoints', '260 balls drawn the cheap way: [RigidBodyPoints] and [RepeatMeshAtPoints] with one sphere mesh, instead of rebuilding geometry every frame.', 'CallToAction')
    return s


def sdf_field():
    s = Scene('PhysicsSdfFieldExample', 'Raymarched SDF terrain and a moving blob that physics bodies and meshes collide with.',
              "The ground here is not a mesh at all: it is an SDF field, a formula on the GPU, drawn with [RaymarchField]. Physics runs on "
              "the CPU, so the field reaches it by being sampled: [GridPoints] lays a grid over it, [SampleFieldPoints] writes the "
              "field's distance into every point, [ReadPointsFromGpu] brings them to the CPU and [SdfPointsToGeometry] extracts the surface "
              "as the Geometry of a static [RigidBody].\n\n"
              "The hilly terrain is sampled once. The blob is a plain sphere, so its collider is a Kinematic sphere [RigidBody] "
              "driven by the same [AnimVec3] as the field: it follows the blob exactly and shoves the bodies around. Boxes come from [CubeGeometry], tori and icosahedra from GPU meshes through "
              "[MeshToGeometry]. Raymarching writes depth, so the meshes sit correctly in the raymarched scene.\n\n"
              "AKA: sdf collider, field collider, raymarch, implicit surface, signed distance, terrain\n\n"
              "Tips\n" + TIPS_COMMON + "\n- The sample grid sets the collider's detail: Count times Size must cover the shape.\n"
              "- Reading a grid back and rebuilding a mesh collider every frame is expensive. Moving shapes that are spheres, boxes or "
              "capsules collide best as Kinematic primitives that follow the same motion, like the blob.")
    # Fields
    s.op('floor', 'PlaneSDF', 0, 0, name='Floor', Center=(0.0, 0.0, 0.0), Axis=1)
    s.op('hillA', 'SphereSDF', 0, 1, name='HillA', Center=(-3.5, -1.6, -1.5), Radius=3.0)
    s.op('hillB', 'SphereSDF', 0, 2, name='HillB', Center=(3.0, -2.4, -2.5), Radius=3.4)
    s.op('hillC', 'SphereSDF', 0, 3, name='HillC', Center=(0.5, -2.8, 3.5), Radius=3.2)
    s.op('terrain', 'CombineSDF', 1, 1, name='Terrain', CombineMethod=4, K=1.2)
    for hill in ['floor', 'hillA', 'hillB', 'hillC']:
        s.link(hill + '.Result', 'terrain.InputFields')
    s.op('blobMotion', 'AnimVec3', 0, 5, name='BlobMotion', Rates=(0.12, 0.25, 0.07), Amplitudes=(3.5, 0.5, 2.5), Offsets=(0.0, 0.3, 0.0))
    s.op('blob', 'SphereSDF', 1, 5, name='Blob', Radius=1.4)
    s.link('blobMotion.Result', 'blob.Center')
    s.op('shape', 'CombineSDF', 2, 3, name='TerrainAndBlob', CombineMethod=4, K=0.6)
    s.link('terrain.Result', 'shape.InputFields')
    s.link('blob.Result', 'shape.InputFields')
    s.op('raymarch', 'RaymarchField', 3, 3, name='Raymarch', Color=(0.62, 0.66, 0.6, 1.0), AmbientOcclusion=(0.0, 0.0, 0.0, 0.6),
         AoDistance=1.5, WriteDepth=True)
    s.link('shape.Result', 'raymarch.SdfField')

    # Sampling: the terrain never changes, so it is read back once
    s.op('terrainGrid', 'GridPoints', 3, 0, name='TerrainGrid', CountX=64, CountY=14, CountZ=64, Size=(0.25, 0.25, 0.25), Scale=1.0,
         Center=(0.0, 0.4, 0.0))
    s.op('terrainSample', 'SampleFieldPoints', 4, 0, name='SampleTerrain')
    s.op('terrainRead', 'ReadPointsFromGpu', 5, 0, name='TerrainToCpu', UpdateContinuously=False, MaxCount=0)
    s.op('terrainSurface', 'SdfPointsToGeometry', 6, 0, name='TerrainSurface')
    s.op('terrainBody', 'RigidBody', 7, 0, name='TerrainCollider', Kind=1, Friction=0.6)
    s.link('terrainGrid.OutBuffer', 'terrainSample.Points')
    s.link('terrain.Result', 'terrainSample.Field')
    s.link('terrainSample.Result2', 'terrainRead.PointBuffer')
    s.link('terrainRead.Output', 'terrainSurface.Points')
    s.link('terrainSurface.Result', 'terrainBody.Geometry')

    # The blob is a sphere, so its collider is a kinematic sphere that follows the same motion: exact, and far cheaper
    # than sampling and rebuilding a mesh collider on every frame.
    s.op('blobBody', 'RigidBody', 7, 6, name='BlobCollider', Kind=2, Shape=1, Radius=1.4, Friction=0.4)
    s.link('blobMotion.Result', 'blobBody.Position')

    # Dynamic bodies
    s.op('box', 'CubeGeometry', 3, 8, name='Box', Size=(0.5, 0.5, 0.5))
    s.op('boxPoints', 'ScatterPointsInVolume', 5, 8, name='BoxPoints', Count=40, Center=(0.0, 7.0, 0.0), Size=(10.0, 3.0, 8.0), Seed=2)
    s.op('boxes', 'RigidBody', 7, 8, name='Boxes', Friction=0.5)
    s.link('box.Geometry', 'boxes.Geometry')
    s.link('boxPoints.ResultList', 'boxes.Points')
    s.op('torusMesh', 'TorusMesh', 3, 9, name='TorusMesh', Radius=0.4, Thickness=0.15, Segments=(32, 16))
    s.op('torusGeometry', 'MeshToGeometry', 4, 9, name='TorusGeometry')
    s.link('torusMesh.Data', 'torusGeometry.Mesh')
    s.op('torusPoints', 'ScatterPointsInVolume', 5, 9, name='TorusPoints', Count=20, Center=(0.0, 9.5, 0.0), Size=(10.0, 3.0, 8.0), Seed=6)
    s.op('tori', 'RigidBody', 7, 9, name='Tori', Friction=0.5)
    s.link('torusGeometry.Result', 'tori.Geometry')
    s.link('torusPoints.ResultList', 'tori.Points')
    s.op('icoMesh', 'IcosahedronMesh', 3, 10, name='IcoMesh', Scale=0.35)
    s.op('icoGeometry', 'MeshToGeometry', 4, 10, name='IcoGeometry')
    s.link('icoMesh.Data', 'icoGeometry.Mesh')
    s.op('icoPoints', 'ScatterPointsInVolume', 5, 10, name='IcoPoints', Count=30, Center=(0.0, 12.0, 0.0), Size=(10.0, 3.0, 8.0), Seed=9)
    s.op('icos', 'RigidBody', 7, 10, name='Icosahedra', Friction=0.5, Bounciness=0.3)
    s.link('icoGeometry.Result', 'icos.Geometry')
    s.link('icoPoints.ResultList', 'icos.Points')

    s.op('world', 'PhysicsWorld', 9, 5, name='World')
    for body in ['terrainBody', 'blobBody', 'boxes', 'tori', 'icos']:
        s.link(body + '.OutBody', 'world.Bodies')
    # The raymarch comes first: it fills the depth buffer that the meshes drawn after it are tested against.
    draws = ['raymarch.DrawCommand',
             draw_bodies(s, 'boxesView', 'world', 'boxes', 11, 6, C['orange'], 'Boxes'),
             draw_mesh_at_bodies(s, 'toriView', 'world', 'tori', 'torusMesh.Data', 11, 7, C['teal'], 'Tori'),
             draw_mesh_at_bodies(s, 'icosView', 'world', 'icos', 'icoMesh.Data', 11, 8, C['yellow'], 'Icosahedra')]
    render_rig(s, draws, 15, camera=(0.0, 8.0, 15.0), target=(0.0, 0.5, 0.0), fog=70.0)

    s.section('1  The SDF world: terrain and a moving blob', ['floor', 'hillA', 'hillB', 'hillC', 'terrain', 'blobMotion', 'blob', 'shape', 'raymarch'], S['source'])
    s.section('2  Colliders: the sampled terrain and a kinematic blob', ['terrainGrid', 'terrainSample', 'terrainRead', 'terrainSurface', 'terrainBody',
                                                                          'blobBody'], S['world'])
    s.section('3  Geometry and meshes falling on it', ['box', 'boxPoints', 'boxes', 'torusMesh', 'torusGeometry', 'torusPoints', 'tori',
                                                        'icoMesh', 'icoGeometry', 'icoPoints', 'icos'], S['bodies'])
    s.tour('terrain', 'Hills from spheres, smoothly blended into a floor plane: the terrain is only a formula.', 'Info')
    s.tour('raymarch', '[RaymarchField] draws the field and writes depth, so meshes drawn afterwards sit inside the scene correctly.', 'InfoFor', 'WriteDepth')
    s.tour('terrainGrid', 'To collide with the field, physics samples it on a grid of points.', 'InfoFor')
    s.tour('terrainSample', '[SampleFieldPoints] writes the field distance into every point.', 'InfoFor')
    s.tour('terrainRead', 'The terrain never changes, so its samples are read back once.', 'InfoFor', 'UpdateContinuously')
    s.tour('terrainSurface', '[SdfPointsToGeometry] extracts the surface: the collider geometry.', 'InfoFor')
    s.tour('blobMotion', 'The blob wanders around with [AnimVec3].', 'InfoFor')
    s.tour('blobBody', 'The blob collides as a Kinematic sphere at the same position: no sampling or mesh rebuild on any frame.', 'InfoFor', 'Kind')
    s.tour('tori', 'Tori from a GPU mesh through [MeshToGeometry]; Auto splits them into hulls.', 'InfoFor')
    return s


def volume_gravity():
    s = Scene('PhysicsVolumeGravityExample', 'A torus volume pulls 300 boxes onto its surface, with gravity turned off.',
              "A volume can be a force as well as a collider. [VolumeToField] turns the torus volume into a vector field, its gradient, "
              "and a [RigidForce] of type Field samples that field at every body. Negative Strength pulls the boxes towards the surface "
              "instead of pushing them away. The same volume, meshed with [VolumeToMesh], is the collider they land on. World gravity is zero.\n\n"
              "AKA: gravity well, attractor, magnet, field force, volume gradient\n\n"
              "Tips\n" + TIPS_COMMON + "\n- Any [VectorField] works as a force: noise, curl, or a field built from SDF operators.")
    s.op('torus', 'VolumePrimitive', 0, 1, name='TorusVolume', Shape=3, Size=(6.0, 1.6, 1.0), VoxelSize=0.2, BandWidth=24.0)
    s.op('surface', 'VolumeToMesh', 1, 1, name='TorusSurface')
    s.op('collider', 'RigidBody', 3, 1, name='TorusCollider', Kind=1, Friction=0.5)
    s.link('torus.Volume', 'surface.Volume')
    s.link('surface.Geometry', 'collider.Geometry')
    s.op('gradient', 'VolumeToField', 1, 3, name='Gradient', Scale=1.0)
    s.op('pull', 'RigidForce', 3, 3, name='PullToSurface', ForceType=3, Strength=-9.0)
    s.link('torus.Volume', 'gradient.Volume')
    s.link('gradient.Vectors', 'pull.Field')
    s.op('debrisPoints', 'ScatterPointsInVolume', 1, 5, name='DebrisPoints', Count=300, Size=(11.0, 11.0, 7.0), Seed=6)
    s.op('debris', 'RigidBody', 3, 5, name='Debris', Shape=0, Size=(0.25, 0.25, 0.25), LinearDamping=0.6)
    s.link('debrisPoints.ResultList', 'debris.Points')
    s.op('world', 'PhysicsWorld', 5, 3, name='World', Gravity=(0.0, 0.0, 0.0))
    s.link('collider.OutBody', 'world.Bodies')
    s.link('debris.OutBody', 'world.Bodies')
    s.link('pull.OutForce', 'world.Forces')
    draws = [draw_bodies(s, 'torusView', 'world', 'collider', 7, 1, C['teal'], 'Torus'),
             draw_bodies(s, 'debrisView', 'world', 'debris', 7, 3, C['orange'], 'Debris')]
    render_rig(s, draws, 11, camera=(0.0, 3.0, 14.0), target=(0.0, 0.0, 0.0), light=(5.0, 9.0, 9.0), fog=60.0)
    s.section('1  One volume, two uses', ['torus', 'surface', 'collider', 'gradient', 'pull'], S['source'])
    s.section('2  Debris in zero gravity', ['debrisPoints', 'debris', 'world'], S['bodies'])
    s.tour('torus', 'One torus volume feeds both the collider and the force.', 'Info')
    s.tour('gradient', '[VolumeToField] outputs the volume\'s gradient as Vectors: they point away from the surface.', 'InfoFor')
    s.tour('pull', 'A Field force samples it at every body; negative Strength pulls towards the surface.', 'InfoFor', 'Strength')
    s.tour('world', 'Gravity is zero, so the torus is the only thing that attracts.', 'InfoFor', 'Gravity')
    return s


def physics_to_volume():
    s = Scene('PhysicsToVolumeExample', 'A pile of falling balls wrapped in one smooth skin through a volume.',
              "Physics output feeds the volume operators too. The world's poses are read back with [ReadPointsFromGpu], "
              "[VolumeFromPoints] splats them into a density volume and [VolumeToMesh] extracts one blobby surface around all of them, "
              "every frame. Think of it as a metaball or liquid look on top of rigid bodies.\n\n"
              "None of this holds up a frame: the read-back is asynchronous and both volume operators work on a background "
              "thread, so the skin trails the balls by a frame or two while playback stays smooth.\n\n"
              "AKA: metaballs, blobby, liquid, skin, splat, points to volume\n\n"
              "Tips\n" + TIPS_COMMON + "\n- A larger Radius on [VolumeFromPoints] melts the balls together; a smaller VoxelSize gives a smoother skin and costs more.")
    ground(s, 'ground', 2, 0)
    s.op('ballPoints', 'ScatterPointsInVolume', 0, 1, name='BallPoints', Count=60, Center=(0.0, 4.0, 0.0), Size=(2.5, 6.0, 2.5), Seed=2)
    s.op('balls', 'RigidBody', 2, 1, name='Balls', Shape=1, Radius=0.3, Bounciness=0.2)
    s.link('ballPoints.ResultList', 'balls.Points')
    s.op('world', 'PhysicsWorld', 3, 1, name='World')
    s.link('ground.OutBody', 'world.Bodies')
    s.link('balls.OutBody', 'world.Bodies')
    s.op('poses', 'ReadPointsFromGpu', 5, 2, name='PosesToCpu', UpdateContinuously=True, MaxCount=0, Async=True)
    s.op('density', 'VolumeFromPoints', 6, 2, name='Splat', VoxelSize=0.12, Radius=0.55, Density=1.0, Falloff=1)
    s.op('skin', 'VolumeToMesh', 7, 2, name='Skin', Isovalue=0.35)
    s.op('skinMesh', 'GeometryToMesh', 8, 2, name='SkinMesh')
    s.op('skinDraw', 'DrawMeshWithShadow', 9, 2, name='DrawSkin', Color=(0.3, 0.6, 0.95, 1.0))
    s.link('world.OutBuffer', 'poses.PointBuffer')
    s.link('poses.Output', 'density.Points')
    s.link('density.Volume', 'skin.Volume')
    s.link('skin.Geometry', 'skinMesh.Geometry')
    s.link('skinMesh.Buffers', 'skinDraw.Mesh')
    draws = [draw_bodies(s, 'groundView', 'world', 'ground', 5, 0, C['ground'], 'Ground'), 'skinDraw']
    render_rig(s, draws, 11, camera=(0.0, 4.0, 8.0), target=(0.0, 0.8, 0.0), light=(4.0, 9.0, 5.0))
    s.section('1  Falling balls', ['ground', 'ballPoints', 'balls', 'world'], S['bodies'])
    s.section('2  Poses into a volume, volume into a skin', ['poses', 'density', 'skin', 'skinMesh', 'skinDraw'], S['draw'])
    s.tour('poses', 'The world\'s OutBuffer holds every body\'s pose. Async reads it back every frame without waiting for the GPU.', 'InfoFor', 'Async')
    s.tour('density', '[VolumeFromPoints] splats a sphere of density at every point.', 'InfoFor', 'Radius')
    s.tour('skin', 'One surface around all the balls: lower the Isovalue for a fatter skin.', 'InfoFor', 'Isovalue')
    return s


SCENES = [physics_world, rigid_body, rigid_joint, rigid_force, rigid_body_geometry, rigid_body_points, mesh_to_geometry, volumes, sdf_field,
          volume_gravity, physics_to_volume]

if __name__ == '__main__':
    root = sys.argv[1]
    gen.load_operators(root)
    for make in SCENES:
        scene = make()
        folder = scene.write(root)
        print(f'{scene.name}: {len(scene.ops)} ops, {len(scene.links)} links, {len(scene.tours)} tour points')
