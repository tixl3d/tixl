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

## Step: Unbinding stops the presentation

**Action:**
Right-click the bound `P1` entry again and choose "Stop presenting".

**Expected:**
- The secondary render window closes.
- The binding is removed from `.meta/outputs.machine.json`.
- The menu row shows the plain `P1  ·  …` label again.
