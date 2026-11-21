---
id: output-setup-laser
title: Output Setup — Laser Output, Devices and Calibration
scope: output-window
tags: [laser]
added: 2026-10-25
added-in-version: 4.4
prerequisites:
  - A writable project is open whose active setup has one laser output, with a [LaserCamera] placed in the graph and its OutputRef set to that output.
  - A laser device, or a bench substitute - a PONK endpoint with Multicast off and any unicast address is enough to see the plug connect without hardware.
  - The graph window, the Parameter window and one output window with the Flow Outliner shown are visible.
related-help:
  - ../.help/docs/using/OutputSetup.md
---

Covers the laser output card end to end: the aim rectangle before calibration, **Send**, the
scanner camera's calibration solve, live device settings, several outputs on one plug, the laser
camera's wireframe culling, and its point-budget warning.

## Step: An uncalibrated output streams the aim rectangle

**Action:**
With the laser output selected, click the **LASER** button in the app bar, then look at the output's card.

**Expected:**
- The **LASER** button turns armed and stays orange (an uncalibrated laser output exists).
- **Laser frame** shows a green rectangle at the size and place of the **Safety → setup rectangle** values, not the camera's scene.
- The device bound under **Local Bindings** reports that it is streaming.

## Step: Send mutes the output and keeps the device

**Action:**
Untick **Send** at the top of the laser output card, wait a second, then tick it again.

**Expected:**
- While unticked the device stops receiving frames and the card says so, but the plug stays connected - no discovery or reconnect status appears.
- Ticking it again resumes streaming within a frame or two; nothing about the binding changes.

## Step: A laser camera freezes like a send

**Action:**
Select the [LaserCamera] in the flow outliner's CONTENT column, untick **Update** on its card, then animate something in its **Scene** (or move an object in it).

**Expected:**
- The outliner item reads "laser · frozen" and the card names the output it streams to with its point count.
- **Laser frame** and the device keep showing the last frame; the scene's changes no longer reach the laser, and the editor's frame time drops.
- Ticking **Update** again resumes at once. The change is a normal undoable edit of the camera's **Update** input.

## Step: A camera reference takes over the laser camera's aim

**Action:**
1. Add a [Camera] to the graph and wire its **Reference** output into the [LaserCamera]'s **CameraReference** input.
2. Move the [Camera]'s **Position** and **Target** - or use an [OrbitCamera] with some **Spin Rate** instead.
3. Move the [LaserCamera]'s own **Position** and **Target**, then toggle its **Use Rotation**.

**Expected:**
- The **Laser frame** follows the referenced camera live, including while it animates; the laser camera's own aim inputs change nothing while the reference resolves.
- The drawn view frame sits on the referenced camera, and the laser camera's position/target gizmo handles disappear - the aim belongs to the referenced camera.
- The frame stays **square** (the scanner field): a 16:9 camera gives a square crop of its view, not a horizontally squeezed picture.
- Removing the connection returns the camera to its own aim. A connected output that is not a camera makes the node report `Camera reference is not usable - using this op's own aim`.

## Step: The calibration solve replaces the manual aim

**Action:**
1. In **Scanner Camera**, note the current **Laser frame** drawing and the **Position/Target/Scan angle** rows.
2. Under **Calibration**, click **Add Point** six times.
3. For each row, set **Stage (m)** to a mark in the venue and **Field** to where the beam hits it - use at least two non-parallel planes (a wall and the floor, or two walls).
4. Click **Solve**, then look at the frame and at the graph's camera view.
5. Click **Clear Solve**, then `Ctrl+Z` twice.

**Expected:**
- With fewer than six points **Solve** is disabled and the card says how many more are needed.
- After **Solve** the card reads `Calibration solve active (residual N px) - it overrides the manual aim`, and the **Laser frame** drawing changes to the solved camera's view (the manual Position/Target rows no longer describe it).
- The residual is small (a few pixels) when the entered field positions match where the beam actually landed.
- **Clear Solve** returns to the manual aim at once, and the points stay in the list. `Ctrl+Z` restores the solve, another `Ctrl+Z` clears it again.

## Step: Device settings apply while streaming

**Action:**
With the laser output streaming, select the bound laser plug under **Local Bindings** and drag its **Brightness ceiling** down to about half.

**Expected:**
- The plug reconnects once, about a quarter second after the drag ends (not once per frame while dragging), and then streams again.
- The device's output is dimmer with the new ceiling; raising it back brightens the beam without a restart.

## Step: Two outputs share one plug

**Action:**
Add a second laser output (`+` on **OUTPUTS** → **Laser**), then drag it onto the same laser plug in **LOCAL BINDINGS**.

**Expected:**
- Both laser outputs stay listed under that plug - the first one is not unbound.
- The plug reports one connection; each output keeps its own frame (**Laser frame** differs per output).

## Step: A dense frame stays real time

**Action:**
Feed the [LaserCamera] a dense mesh scene (`SphereMesh` with the default 64 × 32 segments into a [DrawMesh], its command into the camera's **Scene** input), confirm its **OutputRef** names the calibrated laser output, and watch the editor's frame rate and the **Laser frame** preview for ten seconds.

**Expected:**
- The editor stays at interactive frame rate (well above 30 fps on a laptop-class GPU) - the frame has about 19 000 points and the optimizer must not stall it.
- The preview animates smoothly and the device keeps streaming; no `frame watchdog` warning appears.

## Step: Wireframe culling reads a mesh as outline and folds

**Action:**
Feed the [LaserCamera] a `CubeMesh` through [DrawMesh] into its **Scene** input and look at the **Laser frame** preview. Then tick the camera's **BackfaceCulling** and rotate the camera around the cube, and finally raise **CreaseAngle** to 30° while a `SphereMesh` (not a cube) feeds the scene.

**Expected:**
- At the defaults the cube shows its face borders without triangle diagonals, and shared borders are not brighter than outer ones (each edge is scanned once, even though the cube's faces have separate vertices).
- With **BackfaceCulling** on, only the edges of the faces turned toward the camera are scanned; rotating the camera brings other faces forward and the hidden ones disappear.
- Raising **CreaseAngle** thins a curved mesh's wireframe: the sphere keeps its silhouette and sharp folds while interior tessellation lines drop out, and the camera's point count falls with it.

## Step: An over-budget scene warns instead of flickering

**Action:**
Give the [LaserCamera] a dense scene - several [DrawMesh] spheres and **MaxEdgeStep** down at 500 - until the scene clearly holds more points than the output's frame budget, then select the camera in the flow outliner's CONTENT column and watch the **Laser frame** for a few seconds. Finally raise **MaxEdgeStep** back up.

**Expected:**
- The camera's outliner item shows a warning and its status message reads "Over budget: the frame is capped at ... points - the excess is dropped."
- The frame stays stable: the same parts of the picture stay missing while the scene stands still, nothing flickers in and out.
- Raising **MaxEdgeStep** (or ticking **BackfaceCulling**) clears the warning and the status returns to the point count.

## Step: Safety masks blank the beam where drawn

**Action:**
On a calibrated laser output with content streaming, click **Add mask** in the card's **Masks** section, click its **Edit** button, and drag the four corners in the **Laser frame** preview over a region that must never emit. Then click the mode button so it reads **Allow inside**, and finally untick **Enabled** and save/reload the project.

**Expected:**
- With **Blank inside**, the beam goes dark inside the drawn quad and the geometry keeps moving through it - no lit line crosses the masked area where the content's path enters it; the card's safety status shows "mask blanked N".
- With **Allow inside**, only the inside of the quad lights up; the rest of the frame goes dark.
- Unticking **Enabled** brings the full frame back; masks survive a project reload and stay where drawn when the keystone changes. The safety stage applies them after the warp, so they land exactly where drawn regardless of content.

## Step: A dimming mask and the rendered safety shapes

**Action:**
Switch the mask back to **Blank inside** and drag its **level** field to 0.05, then tick **Render shapes on the laser** (with the output calibrated and streaming) and watch both the laser and the frame preview. Raise the mask level back to 0 afterwards.

**Expected:**
- With level 0.05 the content inside the mask keeps scanning at a faint 5% brightness instead of going dark - the reduced-level audience-scanning mode; the safety status still counts the masked points.
- With **Render shapes on the laser**, faint outlines appear on the real projection: the safe-zone rectangle in green, the enabled masks in orange, at the **Shape level** brightness. They follow a keystone change (they are drawn after the warp) and stay exactly over the regions they protect.
- Raising the mask level back to 0 restores the hard blank.

## Step: Color shift lines up diodes that react at different speeds

**Action:**
On a calibrated laser output streaming content with fast moving colored lines (a [LaserCamera] scene of lines whose colors change along the stroke), set **Color shift (R G B)** to 0, 3, 0 and watch the projection and the **Laser frame** preview. Then set it to 0, -3, 0, then drag a value past 32, then click the row's reset button.

**Expected:**
- At 0, 3, 0 the green component trails the beam by three points: the color boundaries along each stroke move with it, and the first green points of a stroke that had green right at its start go dark.
- At 0, -3, 0 green leads instead and the boundaries move the other way; a blanked travel move between strokes never lights up under either setting.
- Values clamp at 32 either way, are whole points, and undo/redo as one edit. The reset button returns all three to 0 and the frame to its unshifted look. The setting survives a project reload.
