---
id: shadows-in-draw-mesh
title: Shadows with DrawMesh and SetShadow
scope: rendering
tags: [rendering, lighting]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - A sphere above a plane, each drawn with [DrawMesh], and a [Camera] looking at them.
---

[SetShadow] renders a shadow map from a light, and [DrawMesh] inside it receives the shadow. [DrawMeshWithShadow] is
obsolete but keeps working.

## Step: DrawMesh without SetShadow

**Action:**
Connect the draws to a [SpotLight] without [SetShadow].

**Expected:**
- The scene is lit by the spot cone; no shadow and no darkening anywhere.

## Step: Shadow from the light

**Action:**
Insert a [SetShadow] between the draws and the [SpotLight]. Leave Mode on FromLight. Then move the spot light with its
gizmo.

**Expected:**
- The sphere casts a shadow inside the cone, with soft edges and no striped self-shadowing.
- The shadow follows the light in the same frame.

## Step: Pick the light

**Action:**
Add a [PointLight] between [SetShadow] and the [SpotLight]. Open the SourceLight dropdown.

**Expected:**
- The dropdown lists "1st Light", "2nd Light" and both lights by name.
- With "1st Light" the shadow now comes from the point light, aimed at LightTarget. Picking the spot light by name brings
  the spot shadow back, and it stays when the lights are reordered.

## Step: Directional light and Manual mode

**Action:**
Replace the lights with a [DirectionalLight]. Then switch Mode to Manual and move LightPosition.

**Expected:**
- The directional shadow covers AreaSize around LightTarget and doesn't change size with distance.
- In Manual mode the shadow is cast from LightPosition, independent of the light.
- A [SetShadow] without any light shows a warning on the operator and falls back to LightPosition.

## Step: Parameters and output

**Action:**
Change ShadowColor to dark blue with alpha 0.5, then alpha 0. Set NormalOffset to 0. Select the ShadowMap output.

**Expected:**
- The shadow turns blue and half as strong; with alpha 0 it disappears.
- With NormalOffset 0, lit surfaces show striped self-shadowing; the default of 3 removes it.
- The ShadowMap output shows the depth map seen from the light.

## Step: Gizmos and cut-outs

**Action:**
Put a [Transform] between the sphere's [DrawMesh] and [SetShadow] and select it. Then set AlphaCutOff on a [DrawMesh]
that uses a texture with transparent parts.

**Expected:**
- The transform gizmo is drawn once, not a second time from the light's view.
- The cut-out parts don't cast shadows (draws with alpha cut-off keep their pixel shader in the shadow pass).

## Step: Obsolete DrawMeshWithShadow and examples

**Action:**
Open [SetShadowExample], then an older project that uses [DrawMeshWithShadow]. Run both on Vulkan and on D3D11.

**Expected:**
- Both show shadows. [DrawMeshWithShadow] is marked obsolete in search and keeps its own shadow settings.
