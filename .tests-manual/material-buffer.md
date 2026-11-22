---
id: material-buffer
title: Base Color and Material Buffers
scope: rendering
tags: [render-target, pbr, buffers]
added: 2026-11-22
added-in-version: 4.4
prerequisites:
  - A sphere drawn with [DrawMesh] below a [SetMaterial], inside a [RenderTarget].
---

Covers the optional base color and material buffers of [RenderTarget] and [RenderScene].

## Step: Buffers off by default

**Action:**
Look at the BaseColorBuffer and MaterialBuffer outputs of [RenderTarget] with WithMaterialBuffer off.

**Expected:**
- Both outputs are empty. The color image looks as before.

## Step: Material values

**Action:**
Turn WithMaterialBuffer on. Set Roughness 0.3, Metal 0.6, Specular 0.5 and BaseColor orange in [SetMaterial], then show
MaterialBuffer and BaseColorBuffer.

**Expected:**
- MaterialBuffer shows the sphere as one flat color: red about 0.3, green about 0.6, blue 0.5, alpha 1. Rough edges
  facing away from the camera can read slightly rougher.
- The background stays black with alpha 0.
- BaseColorBuffer shows the sphere in flat orange without lighting or reflections.
- Normal and velocity buffers are filled as well.

## Step: Multi-sampling

**Action:**
Set Multisampling to 4 samples.

**Expected:**
- Both buffers keep their values. Along the sphere's outline, alpha of MaterialBuffer fades between 0 and 1.

## Step: Other draw operators

**Action:**
Replace [DrawMesh] with [DrawPointsShaded], [DrawLinesShaded], [DrawMeshAtPoints] and [RaymarchField] in turn.

**Expected:**
- Each one fills both buffers with its material.
- [DrawMeshUnlit], [DrawPoints] and other unlit draws leave the material alpha at 0.

## Step: Render bundle

**Action:**
Turn WithMaterialBuffer on in [RenderScene] and connect its output to an image effect.

**Expected:**
- The output's caption ends with +DNVM.
- With the input off it ends with +DNV, as before.
