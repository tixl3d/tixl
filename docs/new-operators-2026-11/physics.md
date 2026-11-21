# Physics

The physics package is five operators and nothing else: bodies, joints and forces are **pure data
producers**, one `PhysicsWorld` operator owns the actual Bepu simulation, and `RigidBodyPoints` picks
one description's bodies back out of the world for drawing. The world collects the
descriptions from the surrounding graph, creates/updates/removes the Bepu bodies and constraints,
steps them at a fixed rate, interpolates the poses for rendering and publishes everything as a point
buffer.

That split is why `RigidBody` and `RigidJoint` have no simulation state of their own: a body's id is
fixed for the lifetime of the operator instance, which is how the world recognises the same body
across evaluations and only rebuilds what actually changed. Joints become solver constraints between
two bodies; a static body cannot be a joint anchor, so use a kinematic body as the fixed point
instead. A body is rebuilt only when its shape, kind or mass changes, and a joint whose settings
change is removed and re-added, so a constraint is never duplicated.

## PhysicsWorld

Owns the Bepu simulation for the whole graph. It gathers the `RigidBody` and `RigidJoint`
descriptions, builds or updates the world, advances it at `FixedRate` with optional substepping, and
publishes the resulting poses as a point buffer that `DrawMeshAtPoints` instances can render. `SimTime`
is the simulated seconds since the last rebuild, useful for syncing or debugging.

- `Gravity` is the world gravity vector (default is Earth-like).
- `FixedRate` is the simulation step rate in Hz; `Substeps` splits each step for stability.
- `SolverIterations` trades solver quality against cost.
- `TimeScale` slows or speeds the simulation without changing the graph time.
- `Interpolate` smooths rendered poses between fixed steps.
- `IncludeStatics` includes static bodies in the published buffer.
- `Multithreaded` and `Deterministic` control the solver's threading/determinism.
- `ContactStiffness` tunes contact softness.
- `Reset` rebuilds the simulation state; `Paused` freezes stepping while keeping the graph running.
- `Forces` takes `RigidForce` descriptions.
- `MaxContacts` is the length of the `Contacts` buffer; `ContactSpeed` is the closing speed from
  which an impact is reported.

*Robustness:* a non-finite input from an upstream graph (NaN or Infinity) cannot poison the
simulation — gravity keeps its last valid value, and `FixedRate`, `TimeScale` and `ContactStiffness`
fall back to their defaults before being clamped. An exception inside the world is logged once and
resets the outputs instead of escaping into the evaluator.

**Outputs:** `OutBuffer` (per-body pose data for the mesh renderers), `BodyCount`, `SimTime`,
`Contacts` and `ContactCount`.

Each point of `OutBuffer` carries the body's pose and scale, the color of the point it was spawned
from, and its current speed in `F2`. Bodies appear in connection order, and the bodies of one
instanced `RigidBody` in the order of its points or geometry parts.

`Contacts` holds this frame's impacts as points: position, an orientation whose Z axis follows the
contact normal, and the impact speed in `F1`. The buffer always has `MaxContacts` points, because
consumers read the count from the buffer size; unused points have zero scale and `F1` = 0. An
impact is reported once, in the frame it happens, so it suits emitting sparks or dust.

*Tip:* the simulation advances with `context.Playback.FxTimeInBars`, so it runs while playback is
stopped — pump frames to see motion.

![PhysicsWorld selected in the bouncing balls example](shots/PhysicsWorld-ui.png)
*Gravity, solver and stepping parameters with the live example.*

![PhysicsWorld node and its body buffer](shots/PhysicsWorld-graph.png)
*The world collects the bodies and feeds the draw operator.*

## RigidBody

Describes Bepu bodies: shape, kind, transform, size, mass and material parameters. On its own it is
one primitive body. Two optional inputs turn it into many:

- `Points` (a CPU point list): one body per point. The point's position, rotation and scale place
  and size the body relative to `Position` and `Rotation`. GPU points go through
  `ReadPointsFromGpu` first. A point that moves teleports its body, like a changed `Position`.
- `Geometry`: the bodies collide as the geometry. `Pieces` decides what its parts become:
  **EachPartIsABody** (default) makes every part its own body, so `VoronoiFracture` → `RigidBody` is
  a shattering object; **WholeObject** keeps all parts together as one body, which is what an
  imported model with several pieces wants. With `Points` the whole geometry sits at every point. The
  published pose is the pose of the part's pivot, which is what `GeometryToMeshChunks` and
  `DrawMeshChunksAtPoints` expect. A GPU mesh (`LoadGltf`, `CubeMesh`, …) goes through
  `MeshToGeometry` first.

`Shape` picks the collider. **Auto** (the default) is a box without geometry. With geometry it is the
exact triangles for static and kinematic bodies, and for dynamic bodies a convex hull per piece that
is **split into several hulls where the piece is concave**: a torus keeps its hole, a cup its hollow,
a letter its counters. The hulls of one piece move as one Bepu compound body. `MaxHulls` caps how
many hulls one part is split into (1 turns splitting off). **ConvexHull** wraps every part in exactly
one hull, the cheapest choice for shapes that are already convex; **ConvexDecomposition** splits even
mildly concave parts; **Mesh** uses the exact triangles. With geometry connected, a primitive `Shape`
behaves like Auto. A volume collides the same way: `VolumeToMesh` → `Geometry`, `Kind` Static.

The split is an approximate convex decomposition: the part is voxelized as a solid (overlapping shells
count as one solid), then cut along axis planes, always the piece whose hull wastes most volume,
where the two halves' hulls waste least. Each hull is built from the real surface inside it, so outer
faces stay exact. It runs when the geometry changes and takes a fraction of a second for a typical
part. It runs on a worker thread, from a copy of the geometry, so the frame never waits for it: a new body appears once
its hulls are ready, and a body whose geometry changes keeps its old hulls until the new ones are done. Animating the
geometry of a dynamic body still rebuilds it at every change, so don't.

**Mass:** `MassMode` Auto weighs bodies made from geometry by their collider volume times `Density`,
so a small fracture shard is lighter than a big chunk and a hollow ring lighter than a disc, and gives
primitives the fixed `Mass`. Fixed and FromDensity force either rule for every body.

`CollisionGroup`: bodies with the same non-zero group pass through each other.

- `Shape` selects Auto, box, sphere, capsule, cylinder, convex hull, convex decomposition or mesh.
- `Pieces` and `MaxHulls` shape geometry bodies, as described above.
- `Kind` is dynamic, kinematic or static.
- `Position`, `Rotation` place the body; `Size`, `Radius`, `Length` dimension it.
- `Mass` sets the dynamic mass (kinematic/static ignore it); `MassMode` and `Density` weigh
  bodies by their volume instead.
- `LinearVelocity` / `AngularVelocity` give it an initial push.
- `Friction` and `Bounciness` are the contact material.
- `LinearDamping` / `AngularDamping` bleed off motion.
- `GravityScale` scales gravity for this body (0 = floaty, negative = inverted).
- `Enabled` removes it from the simulation without deleting the operator.

**Output:** `OutBody` — the description consumed by `PhysicsWorld` and by joints.

*Tip:* render the bodies by connecting instances of the same mesh through `TransformMesh` to
`DrawMeshAtPoints`, or draw a single mesh and let the buffer place it per body.

![RigidBody selected in the bouncing balls example](shots/RigidBody-ui.png)
*One of the five ball descriptions.*

![RigidBody node](shots/RigidBody-graph.png)
*A body wired into `PhysicsWorld`.*

## RigidJoint

Declares a constraint between two bodies: **BallSocket**, **Hinge**, **Weld** or **DistanceServo**.

`Mode` decides what is linked when body A stands for many bodies:

- **Pair** (default): A to B, instance by instance. A single body on one side is shared by all
  instances of the other.
- **Chain**: consecutive instances of A — a rope from a line of points.
- **Nearby**: every two instances of A that start within `LinkDistance` — nets and clumps.
- **World**: every instance of A is held where it starts. With **Weld** its rotation is held too.

Chain, Nearby and World ignore body B and anchor themselves from the starting positions, so the
linked bodies keep their initial arrangement.

`AutoAnchor` (Pair mode) puts the joint at body B's starting position and works out A's anchor and
axis from where the bodies start, so one joint fits every instance of B — four wheels on one chassis.
`HingeAxisB` is then the axis.

`MotorSpeed` (Hinge) drives the pair around the hinge axis, in radians per second. `SwingLimit`
(BallSocket) is the largest angle in degrees between `HingeAxisA` and `HingeAxisB`, which keeps a
limb from folding back.

`InstanceIndex` narrows Pair and World to one instance: World with `InstanceIndex` 0 hangs a rope by
its first link. -1 means every instance, and a number past the end means the last one.

Like `RigidBody` it is pure data — `PhysicsWorld` creates, updates and removes the Bepu constraint.
Both bodies must be dynamic or kinematic; a static body cannot be an anchor, so use a kinematic body
as the fixed point.

- `BodyA` / `BodyB` are the two constrained bodies.
- `JointType` selects the constraint.
- `LocalOffsetA` / `LocalOffsetB` are the anchor points in each body's local space; for a ball socket
  they are the same world point seen from each body.
- `HingeAxisA` / `HingeAxisB` are the hinge axes in each body's local space.
- `Frequency` / `DampingRatio` tune the servo's spring.
- `TargetDistance` is the rest length for the distance servo.
- `Enabled` removes the constraint without deleting the operator.

**Output:** `OutJoint`. Chain several joints to build ropes, bridges, ragdolls and mechanisms.

![RigidJoint selected in the rope bridge example](shots/RigidJoint-ui.png)
*A hinge constraint between two bridge planks.*

![RigidJoint node](shots/RigidJoint-graph.png)
*Joints linking the bridge segments.*

## RigidBodyPoints

The world publishes all its bodies in one point buffer, which a draw operator can only draw with one
mesh. `RigidBodyPoints` takes the world's `OutBuffer` and one `RigidBody` and outputs a buffer with
just that description's bodies, static and kinematic ones included. Use one per kind of body: chassis
and wheels, paddle and balls, or each of several fractured objects.

**Outputs:** `Points`, `Count`.

## RigidForce

Describes a force for the world's dynamic bodies. **Directional** pushes along `Direction`,
**Radial** pulls towards `Position` (negative `Strength` pushes away), **Vortex** swirls around the
axis through `Position`, and **Field** samples a connected `VectorField` at each body — for
example the `Vectors` output of `VolumeToField`.

- `Strength` is an acceleration; with `ScaleByMass` heavy bodies react less.
- `Radius` and `Falloff` limit Radial and Vortex forces.
- `IsImpulse` applies the force once each time `Trigger` turns on, as a velocity change.
- `CollisionGroup` restricts the force to bodies of that group.

**Output:** `OutForce`, for the `Forces` input of `PhysicsWorld`.

## SDF fields as colliders

Physics runs on the CPU and the SDF field graph runs on the GPU, so a field reaches physics by being
sampled: `GridPoints` (a 3D grid) → `SampleFieldPoints` → `ReadPointsFromGpu` → `SdfPointsToGeometry`
→ the `Geometry` input of a static `RigidBody`. `SdfPointsToGeometry` (in `Lib.geometry`) extracts the
surface from the distances the points carry. Any field works: `SphereSDF`, a `CombineSDF` of several
shapes, or a volume through `VdbToSdfField`. See `PhysicsSdfFieldExample`.

The point grid sets the detail. The field is sampled when the points are read back, so an animated
field needs `UpdateContinuously` on `ReadPointsFromGpu`, and rebuilds the collider when it changes:
a read-back and a new mesh collider on every frame. A moving shape that is a sphere, box or capsule
collides far more cheaply as a Kinematic `RigidBody` driven by the same animation, which is what the
example does for its blob. A Kinematic body animated through `Position` or `Rotation` is moved there
by velocity, so it pushes what it meets instead of jumping into it; a jump of more than 2 m in one
frame is treated as a cut and placed directly.

A triangle-mesh collider builds its search tree on a background thread, which keeps a collider of a
hundred thousand triangles from stalling the frame it appears or changes in. Until it is ready the
body collides as nothing; with `Deterministic` on, or while rendering to a file, it is built at once.

## MeshToGeometry

Reads a GPU mesh back into a `MeshGeometry`, so meshes from `LoadGltf`, `AssimpImport`, `CubeMesh` and
the other mesh operators can feed the geometry operators and `RigidBody`. Every triangle becomes a
face and every mesh chunk a part, so a model loaded in several pieces keeps them apart (use `Pieces`
WholeObject to simulate it as one body). `WeldPoints` joins vertices at the same position, which
reconnects faces a mesh splits for its normals or UVs; normals and texture coordinates are kept per
corner. The readback stalls the GPU briefly and only runs when the incoming mesh changes.

**Inputs:** `Mesh`, `WeldPoints`. **Output:** `Result`. It lives in `Lib.geometry`.

## RigidBodyGeometry

The way back from physics into the geometry operators: the simulated bodies of one `RigidBody` as
geometry. Physics runs on the CPU and so does geometry, so an object needs to be described only once.

- A body made from **geometry** returns that same geometry with every part moved by its body
  (`VoronoiFracture` → `RigidBody` → `PhysicsWorld`, then `RigidBodyGeometry` → `GeometryToMesh`).
  With `Points` connected it returns one copy of the geometry per body.
- A **primitive** body returns a box, sphere, capsule or cylinder of its own size, so it needs no
  separate mesh either.

Every body is one part of the result, and bodies spawned from points carry the point color as a
`Color` part attribute. The result is ordinary geometry, so any geometry operator can follow it.

`ShowColliders` draws bodies made from geometry as the convex hulls they collide as, each hull in a face color
of its own, instead of as their geometry. Use it to check how a concave shape was split.

**Inputs:** `World` (the world's `OutBuffer`), `Body`, `ShowColliders`. **Outputs:** `Result`, `Count`.

*Cost:* the geometry is rebuilt and uploaded every frame. For many identical bodies,
`RigidBodyPoints` with `DrawMeshAtPoints` is cheaper.

## Examples

Eleven example graphs in `Examples.Lib.physics`, each a scene of its own, lit with shadows and laid out the same
way: sections for the sources, the bodies, the world and the drawing, and the shared render set-up on the right.
Each one has a guided tour that walks through its graph step by step. Most are named after the operator they show,
so they open from that operator's help.

| Example | Shows |
| --- | --- |
| `PhysicsWorldExample` | The basics: Bounciness from 0 to 1, a domino run from points, capsules and cylinders, and a spark at every contact |
| `RigidBodyExample` | Geometry bodies: rings that slide down posts because Auto keeps their holes open, and the same word with Pieces EachPartIsABody and WholeObject |
| `RigidJointExample` | A rope from Chain and World joints, a seesaw on a Hinge around a kinematic pivot, a net from Nearby links |
| `RigidForceExample` | A whirlwind from vortex, radial and directional forces, and an impulse blast on Trigger |
| `RigidBodyGeometryExample` | A `VoronoiFracture` cube that shatters, and a ring drawn with ShowColliders |
| `RigidBodyPointsExample` | A car on four motorised wheels into a wall of crates, drawn instanced with `RepeatMeshAtPoints` |
| `MeshToGeometryExample` | GPU meshes (tori, icosahedra, cylinders) as bodies, falling into a bowl carved from volumes |
| `PhysicsVolumesExample` | `bunny.vdb` and a `VolumeCsg` bowl as colliders |
| `PhysicsSdfFieldExample` | A raymarched SDF terrain and a moving SDF blob; geometry and mesh bodies collide with both |
| `PhysicsVolumeGravityExample` | A torus volume's gradient as a Field force, in zero gravity |
| `PhysicsToVolumeExample` | Falling balls wrapped in one skin with `VolumeFromPoints` |

The graphs are generated by `Scripts/example-scenes/physics.py`; their set-ups are also simulated headlessly in
`Tests/Lib.Tests/Physics/PhysicsExampleScenesTests.cs`.
