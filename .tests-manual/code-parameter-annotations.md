---
id: code-parameter-annotations
title: Parameter Labels from Code Comments
scope: shaders
tags: [shaders, parameters]
added: 2026-11-27
added-in-version: 4.4
prerequisites:
  - A project with a [CustomPointShader] connected to an output.
related-help:
  - ../.help/docs/advanced/UsingCustomShaders.md
---

Covers comment lines in the code of a custom shader operator that rename its parameters and set their ranges.

## Step: Label and soft range

**Action:**
Add `// A: Scale 0..1` as the first line of the ShaderCode.

**Expected:**
- Parameter A shows as **Scale (A)** in the Parameter Window.
- Dragging the slider covers about 0 to 1; typing `3` still sets 3.

## Step: Clamped range and tooltip

**Action:**
Add `// B: Twist [-180..180] - rotation in degrees`.

**Expected:**
- B shows as **Twist (B)**, and values can't go below -180 or above 180.
- The tooltip shows "rotation in degrees".

## Step: Marking unused parameters

**Action:**
Add `// C,D: - not needed for this preset`.

**Expected:**
- C and D show as **- (C)** and **- (D)**. They are only dimmed while at their default, like any parameter.
- Their tooltip says "Not used by the code. not needed for this preset".
- Without that line, C and D look as before, even though they have no label.

## Step: Lines that don't match

**Action:**
Add `// Note that A scales the points` and `p.x += A; // C: inline`.

**Expected:**
- Nothing changes: a line needs `Name:` right after `//`, at the start of the line.

## Step: Presets and undo

**Action:**
Save the setup as a preset, apply another preset, then apply yours again. Then remove the `// A:` line and undo.

**Expected:**
- The labels come and go with the preset's code.
- Removing the line turns Scale (A) back into A immediately; undo brings the label back.
