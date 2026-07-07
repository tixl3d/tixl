---
id: parameter-display-presets
title: Parameter Display Presets
scope: parameter-window
tags: [essential, dev]
added: 2026-07-07
added-in-version: 4.2
prerequisites:
  - A writable project with an editable operator that has a Vec2 parameter (e.g. a duplicated [Remap]).
---

Verifies the formatting presets in parameter settings mode, including the Vec2
gizmo and default value they apply.

## Step: Opening the formatting presets

**Action:**
Open your editable operator, enable parameter settings mode in the Parameter
Window (the list icon), select a Vec2 parameter, and click the small settings
icon in the top-right corner of the settings panel.

**Expected:**
- A popup titled "Apply formatting presets..." opens.
- It lists presets like "Rotation", "Translation", "Color", and "Gain & Bias".

## Step: Applying the Gain & Bias preset

**Action:**
Click the "Gain & Bias" preset.

**Expected:**
- The value range is set to 0 … 1 with both clamp toggles enabled.
- The parameter shows the Gain & Bias curve gizmo next to its value fields
  (widen the Parameter Window if the gizmo is not visible).
- The parameter's default value becomes 0.5, 0.5 — resetting the parameter
  (right-click, "Reset to default") returns to these values.

## Step: Applying the Translation preset

**Action:**
Open the presets popup again and click "Translation".

**Expected:**
- The value range is set to -2 … 2 without clamping.
- The gizmo switches to the 2D position control with crosshair lines.
- The default value is unchanged — the preset does not overwrite it.

## Step: Loading legacy Gain & Bias settings

**Action:**
Open a stock operator that uses the gizmo, e.g. [Remap], and check its
`BiasAndGain` parameter in the Parameter Window.

**Expected:**
- The Gain & Bias curve gizmo renders as before (older files store the control
  under the legacy spelling "BiasAndGain", which must still load).
