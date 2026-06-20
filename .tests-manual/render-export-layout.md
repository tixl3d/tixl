---
id: render-export-layout
title: Render Export — Sidebar Layout & Footer
added: 2026-06-20
added-in-version: 4.2
scope: render-export
tags: [essential]
prerequisites:
  - A project is open with an operator that has a Texture2D output selected or pinned in the Output Window (so "Render To File" can render it).
---

Covers the restructured **Render To File** window: a left sidebar splits the settings into **Source**,
**Format & Quality**, and **Output Target**, and a sticky footer holds the one-line summary and the primary
**Render** button. Also checks the restyled shared controls (segmented buttons, checkboxes) that this window
now uses.

## Step: The window has a sidebar with three sections

**Action:**
Open the **Render To File** window.

**Expected:**
- A left sidebar lists **Source**, **Format & Quality**, and **Output Target**.
- The selected entry reads as one connected surface with the lighter content panel to its right; the other
  two entries are muted with no background. Hovering an unselected entry shows a faint highlight.
- The sidebar and the area around it (including the footer) are the darker input-field color; the active
  section's content panel is lighter.

## Step: Clicking a section swaps the content without the window resizing

**Action:**
Click **Source**, then **Format & Quality**, then **Output Target**, watching the content panel.

**Expected:**
- **Source** shows Range, Scale, Start/End, FPS, Resolution and Motion Blur.
- **Format & Quality** shows Render Mode, then (for Video) Codec, Bitrate/estimate and Export Audio.
- **Output Target** shows the Folder, Filename and Auto-increment controls.
- Switching sections does not resize or move the window, and the footer stays put.

## Step: Render Mode and Resolution each appear once

**Action:**
Look for the **Render Mode** toggle and the **Resolution** control across the three sections.

**Expected:**
- **Render Mode** (Video / Image Sequence) appears only in **Format & Quality**.
- **Resolution** appears only in **Source**.
- Toggling **Render Mode** changes which controls **Format & Quality** and **Output Target** show
  (e.g. Codec/Bitrate for Video vs. Format/Subfolder for Image Sequence).

## Step: Segmented buttons render as a rounded pill

**Action:**
In **Source**, look at the **Range** and **Scale** selectors. In **Format & Quality**, look at **Render Mode**.

**Expected:**
- Each is one rounded track; the active option sits inside it as a filled, rounded pill with **bold, bright**
  text. Inactive options are muted with normal-weight text.
- Clicking an option moves the filled pill to it; the track does not change width as you switch.

## Step: Checkboxes show a white checkmark in a rounded box

**Action:**
Toggle **Export Audio** (Format & Quality, Video mode) and **Auto-increment version** (Output Target) on and
off.

**Expected:**
- Each checkbox is a small rounded box; when on it shows a white checkmark, when off it is empty.
- The hit area covers the box and its label; the box brightens slightly on hover.

## Step: The footer shows a one-line summary and a right-aligned Render button

**Action:**
With renderable output, read the footer. Then deliberately clear the **Filename** (Output Target) to make the
settings invalid, and read the footer again.

**Expected:**
- When valid, the footer's left side shows a single muted line like
  `2:12.0s / 1920×1080 / H.264 → ~95 MB / 2 min`, and the blue **Render** button is at the far right with a
  small **open-folder** icon to its left.
- When invalid (empty filename), the footer line turns to the attention color and states the problem, and the
  **Render** button is disabled (its tooltip repeats the reason).
- The open-folder icon is enabled only when the configured output folder exists; clicking it opens that folder.

## Step: During a render the footer shows progress and Cancel

**Action:**
Choose a short range and press **Render**. Watch the footer while it encodes.

**Expected:**
- The footer replaces the summary/Render button with a progress bar, a "… remaining" estimate, and a
  right-aligned **Cancel** button.
- **Cancel** stops the render; the footer returns to the summary + **Render** state.
- The section content above the footer stays visible and the window does not jump in size when the render
  starts or stops.

## Step: A non-renderable output shows a single message

**Action:**
Deselect / unpin the texture output (or select an operator without a Texture2D output) so nothing renderable
is available, then open **Render To File**.

**Expected:**
- Instead of the sidebar and footer, the window shows a single explanatory line (e.g. "The output view is
  empty" or "Select or pin a Symbol with Texture2D output…").
- Restoring a valid texture output brings the full sidebar layout back.
