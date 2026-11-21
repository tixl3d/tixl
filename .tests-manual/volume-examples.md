---
id: volume-examples
title: Volume Examples
scope: volume
tags: [regression]
added: 2026-11-18
added-in-version: 4.4
prerequisites:
  - An empty project is open.
---

Walks through the example graphs in `Examples.Lib.volume`.

## Step: Every example renders

**Action:**
Open each example in `Examples.Lib.volume` and pin its output.

**Expected:**
- Each one renders a lit scene with shadows on the floor within a few seconds.
- The console shows no warnings from volume operators.

## Step: Guided tours

**Action:**
In each example, start the tour.

**Expected:**
- Every step highlights an existing operator or parameter, and the text matches what the graph does.

## Step: Frame time

**Action:**
Open each example in `Examples.Lib.volume` and `Examples.Lib.physics` in turn, play, and watch the frame time in the editor's statistics.

**Expected:**
- After the scene has appeared, every example plays at 16 ms per frame or less on the reference machine.
- `PhysicsToVolumeExample` plays smoothly; the skin around the balls may trail them by a frame or two.

## Step: Parameters never stall the editor

**Action:**
In `VolumeShakingWaterExample`, drag the Amplitude of the Waves [VolumeNoise] back and forth, then set the VoxelSize of the Water [VolumePrimitive] to 0.0001.

**Expected:**
- The editor stays responsive while dragging; the water surface follows a moment later.
- The tiny voxel size logs one warning that the primitive is too large, not one per frame, and the editor keeps running.

## Step: Primitives and voxel size

**Action:**
Open `VolumePrimitiveExample`, select `Cone` and set VoxelSize to 0.1.

**Expected:**
- The cone turns visibly coarse and rounded; setting it back to 0.03 restores it.

## Step: CSG chain

**Action:**
Open `VolumeCsgExample` and switch the Operation of `Intersect` to Union, then back.

**Expected:**
- The left body and the result on the right both update; the drilled holes stay in the result.

## Step: Three ways out

**Action:**
Open `VolumeToPointsExample`.

**Expected:**
- Left: a meshed torus. Middle: a torus of points inside orange tree boxes. Right: a raymarched torus that is hidden correctly behind the floor edge and the points.

## Step: Glass box

**Action:**
Open `NanoVdbRenderExample`, play, and move SmokeDensity, FireIntensity and LiquidAmount of `GlassBox` to 0 one by one.

**Expected:**
- The smoke billows, the flame flickers and the liquid surface moves; each element fades out on its own.

## Step: Files

**Action:**
Open `LoadVdbExample` and turn on LogToConsole on `BunnyInfo`.

**Expected:**
- The bunny stands on the floor next to a smoke plume of similar height.
- The console prints the bunny's voxel count, memory and value range.
