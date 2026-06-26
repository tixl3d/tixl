---
video: tZ1Uz6DmL_c
type: tutorial
date: 2022-07-13
title: "Tooll 3 Tip#010 - Using points to draw Meshes"
duration: 0:03:18
focusesOn: [DrawMeshAtPoints]
---

Opening tip in a points series: explains the eight-value point structure (position, weight W, quaternion orientation), pushes a point list to a GPU buffer, then instances a mesh onto every point and shows how W and randomization drive the result.

## Mentions
- 0:16→0:57 [ui:EvaluationContext] · explained · scripted · Concept · 70% — A point is just eight numbers: an x/y/z position, a single weight value W, and a quaternion orientation (x/y/z/w) rather than Euler angles; this compact layout is what makes point sets cheap to push to the GPU.
- 0:57→1:16 · passing · scripted · Concept · 55% — Wrapping a CPU point list into a GPU buffer is the step that makes the points renderable; visualizing the buffer shows each point's W as a size marker and its orientation as an axis. (Operator name "list-to-buffer" not confidently in vocabulary — left unbracketed.)
- 1:16→2:04 [DrawMeshAtPoints] · explained · scripted · Example · 90% — Wire a loaded mesh plus a point set into one node to stamp a copy of that mesh at every point; because it instances on the GPU it scales to thousands of copies, and a gizmo on the source mesh lets you reposition the template that gets repeated.
- 2:11→2:35 [RandomizePoints] · explained · scripted · Parameters · 85% — Inserted before a draw-at-points node it perturbs each point independently; nudging the position channel scatters the instances, and each channel (position, orientation, W) can be randomized separately for varied placement.
- 2:35→2:59 [DrawMeshAtPoints] [RandomizePoints] · explained · scripted · Tip · 75% — The W weight feeds the per-instance size by default, so randomizing W gives each stamped mesh a different scale — but how W is interpreted (size vs. selection) depends on the consuming operator, not the point itself.
