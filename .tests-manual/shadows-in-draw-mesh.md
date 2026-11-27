---
id: shadows-in-draw-mesh
title: Shadows with DrawMesh and SetShadow
scope: rendering
tags: [rendering, lighting]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - A scene with a [SetSpotLight] or [PointLight] and a few meshes.
---

[DrawMesh] receives the shadow of an enclosing [SetShadow]. [DrawMeshWithShadow] is obsolete but keeps working.

## Step: DrawMesh without SetShadow

**Action:**
Draw a sphere above a plane with two [DrawMesh] operators and a light, without [SetShadow].

**Expected:**
- The scene looks exactly as before: no shadow and no darkening anywhere.

## Step: Insert SetShadow

**Action:**
Put a [SetShadow] above both [DrawMesh] operators. Set LightPosition to the light's position and LightTarget to the
plane's center.

**Expected:**
- The sphere casts a shadow onto the plane, with soft edges a few shadow map texels wide.
- Areas outside the shadow map stay lit (unlike [DrawMeshWithShadow], which darkened them).
- Moving the light position moves the shadow in the same frame.

## Step: Shadow parameters

**Action:**
Change ShadowColor on [SetShadow] to a dark blue with alpha 0.5, then set its alpha to 0. Lower ShadowBias to 0.

**Expected:**
- The shadow turns blue and half as strong; with alpha 0 it disappears.
- With bias 0, lit surfaces show striped self-shadowing; raising ShadowBias or NormalOffset removes it.

## Step: Shader graph fields still work

**Action:**
Inside [SetShadow], connect [SphereSDF] -> [SDFToColor] to FragmentField of one [DrawMesh].

**Expected:**
- The field tints the mesh as without [SetShadow], and the mesh still receives the shadow.

## Step: Obsolete DrawMeshWithShadow

**Action:**
Open [SetShadowExample] (now using [DrawMesh]), then an older project that uses [DrawMeshWithShadow].

**Expected:**
- Both show shadows. [DrawMeshWithShadow] is marked obsolete in search and keeps its own shadow settings.
- Run both on Vulkan and on D3D11.
