---
id: light-types-and-blending
title: Spot and Directional Lights, Per-Target Blending
scope: rendering
tags: [lights, pbr, blending]
added: 2026-11-22
added-in-version: 4.4
prerequisites:
  - A scene with [DrawMesh] (a plane and a sphere) under a [Camera] in a [RenderTarget] with WithNormalBuffer and WithVelocityBuffer on.
---

Covers the spot and directional light operators and blend modes that leave the extra render targets alone.

## Step: Point lights unchanged

**Action:**
Open a project that uses [PointLight] or [SetPointLight] and compare it with TiXL 4.3 or a screenshot from before.

**Expected:**
- The lighting looks the same.

## Step: Spot light

**Action:**
Insert [SetSpotLight] above the draw ops. Change ConeAngle, Softness and Target.

**Expected:**
- A round cone of light falls on the plane and moves with Target.
- ConeAngle widens the cone; Softness 0 gives a hard edge and 1 a soft fade from the center.

## Step: Aim with the rotate gizmo

**Action:**
Select [SetSpotLight] with the Output window showing the scene. Switch the gizmo to Rotate and drag each axis ring,
then the outer screen ring. Press Ctrl while dragging. Switch to Move and drag the light.

**Expected:**
- Rotating swings Target around Position: the cone sweeps across the plane and the Target values change.
- The screen ring turns the aim in the same direction as the mouse moves around the ring.
- Ctrl snaps to 15 degree steps.
- Moving changes only Position; Target stays, so the cone keeps pointing at the same spot.
- Undo restores Target after a rotation in one step.
- [PointLight], [Locator] and other ops with a rotation input rotate as before.

## Step: Directional light

**Action:**
Replace it with [SetDirectionalLight] and rotate its Target with the gizmo.

**Expected:**
- The plane is lit evenly and does not get darker with distance.
- Pointing Direction away from the plane's normal leaves it unlit (apart from the environment).

## Step: Light limit

**Action:**
Chain nine light operators of mixed types.

**Expected:**
- A warning about the limit of 8 appears once; the other lights keep working.

## Step: Additive sprites keep the normal buffer

**Action:**
Draw [DrawPoints] with BlendMode Additive in front of the mesh and look at the render target's normal and velocity
outputs.

**Expected:**
- The normal and velocity outputs show only the mesh; the points do not appear in them.
- With BlendMode Normal or None the points do write into both buffers.
- [ScreenSpaceReflections] and TXAA on an opaque scene look as before.
