---
id: gltf-character-animation
title: glTF Character Animation and Skinning
scope: operators
tags: [essential, user]
added: 2026-09-02
added-in-version: 4.2
prerequisites:
  - An empty project is open.
  - A rigged, animated glTF file with several clips is available. The Khronos sample
    "Fox.glb" is assumed below (clips - 0 Survey, 1 Walk, 2 Run); any similar model works.
related-help:
  - ../.help/docs/using/CharacterAnimation.md
---

Covers the character animation chain: loading a rigged glTF model, inspecting its
skeleton, playing and blending animation clips, procedural joint tweaks, and binding
geometry to a skeleton. Later steps build on the graph from earlier ones.

## Step: Loading a rigged model

**Action:**
In the Graph Window, create a [LoadGltfScene] and set its **Path** to the Fox.glb file.
Connect its **Mesh** output to a [DrawMesh] (with the [LoadGltfScene] **Material**
connected as well) inside your usual camera/render setup.

**Expected:**
- The fox renders as a static mesh in the output.
- The mesh is shaded with visible lighting variation, not a flat silhouette.
- The [LoadGltfScene] operator shows no warning status.

## Step: Inspecting the skeleton

**Action:**
Connect the **SkeletonPoints** output of [LoadGltfScene] to a [DrawPoints].

**Expected:**
- Points appear at the fox's joint positions (spine, legs, tail, head), roughly
  inside the mesh.

## Step: Rendering the rest pose through the skinning path

**Action:**
Create a [PoseToSkinMatrices] and connect [LoadGltfScene] **ResultSetup** to its
**Setup** input, leaving its **Pose** input unconnected. Create a [SkinMesh]; connect
the [LoadGltfScene] **Mesh** and **SkinWeights** outputs and the [PoseToSkinMatrices]
**SkinMatrices** output to it. Route the [SkinMesh] result into the [DrawMesh] instead
of the original mesh.

**Expected:**
- The fox looks exactly as before - same pose, same shading. (The rest pose through
  the skinning path must be indistinguishable from the unskinned mesh.)

## Step: Playing an animation clip

**Action:**
Create a [SampleGltfAnimation]; connect [LoadGltfScene] **ResultSetup** to its
**Setup** and its **Pose** output to the [PoseToSkinMatrices] **Pose** input.
Start playback.

**Expected:**
- The fox animates (clip 0 is an idle/survey motion).
- The motion loops seamlessly when the clip reaches its end.
- Shading follows the deformation - lit and shaded sides update as parts rotate.

## Step: Switching clips and loop behavior

**Action:**
Set **ClipIndex** to 2, then turn **Loop** off and let playback run past the clip's
duration.

**Expected:**
- With ClipIndex 2 the fox runs instead of idling.
- With Loop off, the animation freezes on the clip's last frame instead of wrapping.

## Step: Scrubbing with OverrideTime

**Action:**
Connect a value source (e.g. a [Value] or slider op) to the [SampleGltfAnimation]
**OverrideTime** input and change it slowly between 0 and 2 while playback is stopped.

**Expected:**
- The pose follows the value directly - the value is the clip time in seconds.
- Setting it back to 0 shows the clip's first frame.

## Step: Blending two clips

**Action:**
Create a second [SampleGltfAnimation] with the same **Setup** but a different
**ClipIndex**. Create a [BlendPoses], connect both poses to **PoseA** / **PoseB**,
route its result into [PoseToSkinMatrices], and drag the **Blend** parameter between
0 and 1 during playback.

**Expected:**
- At 0 the fox performs clip A, at 1 clip B.
- In between, the motion is a smooth mix without jitter, collapsing limbs, or
  flipping joints.

## Step: Overriding a single joint

**Action:**
Insert an [OverrideJoint] between a [SampleGltfAnimation] pose and
[PoseToSkinMatrices]. Try a few **JointIndex** values (the fox's spine and head are
in the low indices) and set **RotationOffset** to roughly 45 on one axis.

**Expected:**
- Exactly one body part rotates away from the sampled animation; the rest keeps playing.
- The rotation pivots at the joint and follows the bone's own axes, also while the
  underlying animation moves the joint around.

## Step: Binding a foreign mesh to the skeleton

**Action:**
Create a [BindToSkeleton]; connect a generated mesh (e.g. a [CylinderMesh] roughly
scaled and positioned to overlap the fox's body) to its **Mesh** input and the
[LoadGltfScene] **ResultSetup** to its **Setup** input. Feed its **SkinWeights**
output together with the animated [PoseToSkinMatrices] matrices into a second
[SkinMesh] drawing the cylinder. Adjust **Radius** if the cylinder doesn't deform.

**Expected:**
- The cylinder bends and follows the fox's animation even though it never had
  skin weights of its own.
- Larger **Radius** values make the deformation smoother, smaller ones more rigid.

## Step: Rigging a spline chain

**Action:**
Create a spline or line of points (e.g. [SplinePoints]) and read it back with a
[PointsToCPU] (leave **UpdateContinuously** off, trigger **TriggerUpdate** once);
connect its output to a [SkeletonFromPoints]. Read the same points back with a second
[PointsToCPU] with **UpdateContinuously** enabled and connect it to a [PoseFromPoints]
together with the [SkeletonFromPoints] **Setup**. Route the pose through
[PoseToSkinMatrices] (same **Setup**) into a [SkinMesh] whose mesh and weights come
from a [BindToSkeleton] bound to a tube-like mesh along the chain. Then animate the
spline's shape.

**Expected:**
- While the spline matches the snapshot, the mesh sits in its original shape.
- As the spline animates, the mesh bends to follow it like a snake following its path.
