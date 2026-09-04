---
id: output-setup-presets
title: Setup Output Presets & Display Binding
scope: output-window
tags: [projection-mapping]
added: 2026-07-20
added-in-version: 4.3
prerequisites:
  - A writable project is open.
  - For the display-binding steps, a second monitor (or projector) is connected.
---

Covers the first user-visible slice of the projection-mapping feature: the active
Setup's outputs appearing as named resolution presets in the Output Window, the
per-project setup file in `.meta/`, and presenting an output fullscreen on a display
via a per-machine device binding.

## Step: Default setup is created on first use

**Action:**
With a project open, open an Output Window and click the resolution selector in the
toolbar.

**Expected:**
- Below the regular resolution presets, a "Setup Outputs" group appears.
- It contains a single entry `Default  ·  1920×1080`.
- The project folder now contains `.meta/Setup 1.setup.json`.

## Step: Setup outputs drive the requested resolution

**Action:**
Close the editor. In `.meta/Setup 1.setup.json`, add a second output to the
`Outputs` array (copy the Default entry, give it a new GUID `Id`, set `Name` to
`"Instagram"`, `Kind` to `"Format"` and `CanvasResolution` to `[1080, 1920]`).
Restart the editor, open the project, and pick `Instagram` from the Setup Outputs
group in the resolution selector.

**Expected:**
- The entry reads `Instagram  ·  1080×1920`.
- The output view renders in 9:16 portrait — image operators with resolution 0
  inherit 1080×1920.
- The selection survives an editor restart (per-window state, restored by name).

## Step: Binding an output to a display

**Action:**
Add a third output with `Kind: "Display"` named `"P1"` the same way (or reuse an
existing one). In the Setup Outputs group, right-click the `P1` entry.

**Expected:**
- A context menu lists each connected display with its resolution, e.g.
  "Fullscreen on Display 2 (1920×1080)".

**Action:**
Click the entry for the second display.

**Expected:**
- The secondary render window opens borderless-fullscreen on that display.
- The menu row now reads `P1  →  Display 2`.
- The project folder contains `.meta/outputs.machine.json` with the binding
  (output GUID, display name, index).

## Step: Opening the Setup Panel

**Action:**
In the Output Window toolbar, click `View` and enable "Show Setup Panel".

**Expected:**
- The setup panel appears on the left with sections REFERENCE IMAGES, SURFACES, PROPS,
  OUTPUTS — each with a `+` button.
- The panel title is a dropdown showing the active setup's name (e.g. "Setup 1").
- The OUTPUTS section lists the Default output; bound outputs show their display,
  unbound ones show "unbound".

## Step: Navigating entities in setup mode

**Action:**
Open `View` and choose Output Mode "Setup" (this also opens the panel). Click `+`
next to SURFACES, then click between the new surface and the Default output in
the outline.

**Expected:**
- Selecting the surface shows an info card in the view area (name, kind, size).
- Selecting the Default output opens its editing canvas instead (the output frame; see
  the `corner-pin-editing` set).
- The new surface was saved to the setup file (check the JSON).
- With nothing selected yet, the view shows "Select an entity in the setup panel".

## Step: Windows browse independently

**Action:**
Open a second Output Window, put it in Setup mode too, and select a different
entity in each window's panel.

**Expected:**
- Each window keeps showing its own entity — selections do not affect the other
  window.
- `View → Operator` returns a window to the normal operator output; the graph
  selection renders again.

## Step: Setup switcher duplicates and deletes

**Action:**
In the panel title dropdown, choose "Duplicate current". Then open the dropdown
again and switch between the two setups. Finally delete the copy.

**Expected:**
- The duplicate (e.g. "Setup 1 copy") becomes active and appears in `.meta/`.
- Entity ids in both files are identical (GUID-preserving duplication).
- After deleting, the original setup is active again and the copy's file is gone.

## Step: Unbinding stops the presentation

**Action:**
Right-click the bound `P1` entry again and choose "Stop presenting".

**Expected:**
- The secondary render window closes.
- The binding is removed from `.meta/outputs.machine.json`.
- The menu row shows the plain `P1  ·  …` label again.
