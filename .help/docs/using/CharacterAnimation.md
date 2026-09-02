# Character Animation

TiXL can load rigged and animated glTF models, play their animation clips, blend and
retouch poses, and even rig your own procedural geometry to a skeleton. This page walks
through the whole chain — from loading an animated character to driving particles with
its motion.

## Loading a rigged model

Add a [LoadGltfScene] and point its **Path** to a `.glb` or `.gltf` file that contains a
skeleton (for a quick test, the free Khronos sample models `Fox.glb` or `CesiumMan.glb`
work well). Besides the usual mesh and material outputs, a rigged file fills three more
outputs:

- **SkinWeights** — how strongly each part of the mesh follows each bone.
- **SkeletonPoints** — the skeleton's rest pose as points. Connect it to [DrawPoints]
  to see the joints floating in place over the model.
- **ResultSetup** — the scene setup, which now also carries the skeleton and all
  animation clips found in the file. Most of the operators below take this as input.

## Playing an animation

Three operators form the basic playback chain:

1. [SampleGltfAnimation] reads a clip from the setup and outputs a **pose** — one point
   per joint. Pick the clip with **ClipIndex**; by default the clip follows the
   playback time, and **Loop** wraps it at the clip's end.
2. [PoseToSkinMatrices] turns that pose into the matrices that move each bone.
3. [SkinMesh] deforms the mesh with those matrices and the skin weights.

Wire it up like this:

- [LoadGltfScene] **ResultSetup** → [SampleGltfAnimation] **Setup** and
  [PoseToSkinMatrices] **Setup**
- [SampleGltfAnimation] **Pose** → [PoseToSkinMatrices] **Pose**
- [LoadGltfScene] **Mesh** and **SkinWeights**, plus [PoseToSkinMatrices]
  **SkinMatrices** → [SkinMesh]
- [SkinMesh] **Result** → your usual mesh drawing, e.g. [DrawMesh] with the
  material from [LoadGltfScene]

Press play and the character animates. Because [SkinMesh] outputs a regular mesh, you
can also feed it into any mesh effect — scatter points on the animated surface with
[PointsOnMesh], run it through deformers, or use it in collision setups.

Clip time is measured in seconds. When you connect something to **OverrideTime**, that
value is used as seconds instead of the playback time — handy for scrubbing a clip with
an LFO, an audio level, or a MIDI fader.

## Blending and adjusting poses

A pose is just a list of points, so poses can be mixed before they reach
[PoseToSkinMatrices]:

- [BlendPoses] crossfades between two poses — for example two [SampleGltfAnimation]
  ops playing different clips of the same character. An optional weight mask limits
  the blend to some joints.
- [AdditivePose] layers a clip on top of another one, relative to the rest pose —
  a breathing or sway clip on top of a walk, for instance.
- [OverrideJoint] nudges a single joint from graph inputs: aim a head with a value,
  twist a spine with an LFO, wire a joint to audio.
- [RetargetPose] transfers a pose from one character to another by matching joint
  names, so one animation can drive several differently proportioned rigs.

All of these output a pose again, so they chain freely.

## Rigging your own geometry

Geometry that never had a skeleton can be bound to one:

- [BindToSkeleton] generates skin weights for any mesh (or point buffer) by measuring
  the distance from each vertex to the skeleton's bones in rest pose. **Radius**
  controls how far a bone's influence reaches, **FalloffPower** how softly it fades.
  The result plugs into [SkinMesh] exactly like weights loaded from a file — so you
  can, for example, bind a completely different mesh to the Fox's skeleton and let it
  run with the Fox's animation.
- [SkinPoints] is the point-buffer version of [SkinMesh]: bind a particle cloud or
  point set with [BindToSkeleton] and it deforms along with the rig.

## Building a skeleton from points

Skeletons don't have to come from files. [SkeletonFromPoints] turns a CPU point list —
for example a spline read back with [PointsToCPU] — into a skeleton: each point becomes
a joint, parented to the previous one (separator points split chains). Together with
two more operators this rigs procedural geometry to procedural motion:

1. Take a snapshot of your spline with [PointsToCPU] (leave **UpdateContinuously**
   off, trigger it once) and feed it to [SkeletonFromPoints] — this frozen shape is
   the rest pose.
2. Read the same spline back continuously with a second [PointsToCPU]
   (**UpdateContinuously** on) and feed it to [PoseFromPoints] together with the
   skeleton — this converts the moving spline into a pose.
3. Bind your mesh with [BindToSkeleton], then [PoseToSkinMatrices] → [SkinMesh]
   as usual.

Now the mesh follows the animated spline like a snake following its path.

## Troubleshooting

- **The model renders but doesn't move.** Check that the pose actually reaches
  [PoseToSkinMatrices] — without a pose it outputs the rest pose by design. Also
  check the operator status indicators; the skinning operators report missing or
  mismatched inputs there instead of failing.
- **The mesh follows the wrong clip speed.** Clip time is in seconds while the
  timeline runs in bars, so the playback BPM affects how fast timeline time maps to
  clip time. Connect **OverrideTime** for exact control.
- **Parts of the mesh stay behind when binding procedurally.** Increase the
  **Radius** on [BindToSkeleton] — geometry outside every bone's envelope snaps to
  the nearest bone, which can look rigid.
- **The model appears at the wrong position or orientation.** Skinned meshes are
  posed entirely by their skeleton; draw the [SkinMesh] result directly rather than
  through a scene-node transform, and use a [Transform] afterwards to place it.

## See also

- [Importing assets](ImportingAssets.md)
- [Timeline](Timeline.md)
