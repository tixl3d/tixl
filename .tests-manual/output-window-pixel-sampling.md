---
id: output-window-pixel-sampling
title: Output Window — Sharp Pixels When Zoomed In
scope: output-window
tags: [output-window]
added: 2026-10-25
added-in-version: 4.4
prerequisites:
  - A project is open with an output window visible.
  - A small texture op exists, e.g. a [LinearGradient] or [RyojiPattern2] with its resolution set to 32 × 32.
---

Covers how the output window samples the texture it shows: sharp pixels when magnified, smooth filtering when
shrunk.

## Step: Magnified pixels stay sharp

**Action:**
Pin the small texture op to the output window and zoom in with the mouse wheel until single pixels are
large.

**Expected:**
- Each texel is a crisp square with hard edges, not a soft blur.
- The edges of the image don't pick up colour from the opposite side.

## Step: Shrunk textures stay smooth

**Action:**
Pin a large texture (e.g. a [LoadImage] of a detailed photo) and zoom far out.

**Expected:**
- The image looks smooth, without shimmering moiré or flickering while zooming.

## Step: The rest of the UI is unaffected

**Action:**
Look at operator thumbnails in the graph, the Board cards of the Output Setup, and the editor's text.

**Expected:**
- They look as before: smoothly filtered, with no blocky pixels.
