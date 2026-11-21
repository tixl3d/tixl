---
id: physics-geometry-bodies
title: Physics Bodies from Geometry
scope: physics
tags: [regression]
added: 2026-11-18
added-in-version: 4.4
prerequisites:
  - An empty project is open.
---

Verifies how [RigidBody] turns geometry into colliders: concave shapes split into convex hulls, the
Pieces setting, density-based mass, and GPU meshes through [MeshToGeometry].

## Step: Rings slide down posts

**Action:**
Open `Examples.Lib.physics.RigidBodyExample`, pin its output and play for ten seconds.

**Expected:**
- 24 orange rings fall onto the field of grey posts.
- Several rings that land over a post slide down it and come to rest around it near the floor.
- No ring sinks into the floor or jitters while resting.

## Step: A single hull fills the hole

**Action:**
Select the `Rings` [RigidBody], set Shape to ConvexHull and press Reset on the `World` [PhysicsWorld].

**Expected:**
- Rings now bounce off the post tops or rest on them; none ends up around a post.
- Setting Shape back to Auto and resetting brings the sliding rings back after a short pause while the hulls are rebuilt.

## Step: Fewer hulls

**Action:**
With Shape Auto, set MaxHulls of the `Rings` to 1, reset the world, then set it to 4 and reset again.

**Expected:**
- With 1 the rings behave like ConvexHull.
- With 4 rings slide down posts again, a little less smoothly than with 8.

## Step: Whole object versus parts

**Action:**
In a new graph, connect [CubeGeometry] → [PlaceGeometryAtPoints] fed by a few points so the result has several parts, then into the Geometry of a dynamic [RigidBody] at a height. Add a static ground [RigidBody], a [PhysicsWorld] with both bodies, and draw the bodies with [RigidBodyGeometry] → [GeometryToMesh] → [DrawMesh]. Play, then switch Pieces between EachPartIsABody and WholeObject and reset.

**Expected:**
- EachPartIsABody: the cubes fall and tumble independently.
- WholeObject: the cubes keep their arrangement and fall and tumble as one rigid object.

## Step: Density-based mass

**Action:**
Feed a [VoronoiFracture] of a cube into a dynamic [RigidBody] above a static ground, add a [RigidForce] (Directional, ScaleByMass on) and play.

**Expected:**
- Small shards are pushed visibly further than large chunks.
- Setting MassMode to Fixed makes all shards react the same.

## Step: Collider from a GPU mesh

**Action:**
Connect [IcosahedronMesh] (or a model from [LoadGltf]) → [MeshToGeometry] → the Geometry of a dynamic [RigidBody] and drop it on a static ground.

**Expected:**
- The body falls and rests on its faces, matching the mesh drawn through [RigidBodyGeometry].
- [MeshToGeometry] shows no warning in the console; a model with several meshes arrives as several parts.

## Step: Collider view

**Action:**
In `RigidBodyExample`, select `RingsGeometry` and turn on ShowColliders.

**Expected:**
- The rings are drawn as several faceted convex pieces with a hole in the middle, instead of their smooth surface.

## Step: Guided tours

**Action:**
Open each example in `Examples.Lib.physics` and start its tour.

**Expected:**
- Every tour step highlights an existing operator or parameter, and the text matches what the graph does.
- Every example renders lit, with shadows on the ground, and nothing falls through the floor within 20 seconds.

## Step: Raymarched SDF terrain

**Action:**
Open `PhysicsSdfFieldExample` and play.

**Expected:**
- Boxes, tori and icosahedra land on the raymarched hills, and are hidden correctly where a hill is in front of them.
- The blob wanders across the terrain and shoves the bodies it meets; no body ends up inside it.
