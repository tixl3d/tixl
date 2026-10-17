---
id: trackpad-mouse-switching
title: Switching Between Trackpad and Mouse
scope: graph-window
tags: [input, navigation, linux, macos]
added: 2026-10-17
added-in-version: 4.4
prerequisites:
  - A laptop with a trackpad and a mouse with a notched scroll wheel, both connected.
  - A project is open with a few operators visible in the Graph Window.
  - Settings > Graph window > "Always use touchpad panning" is off.
related-help:
  - ../.help/docs/install/PlatformSupport.md
---

The editor tells a trackpad from a mouse wheel by how it scrolls: a trackpad sends fractions of a
notch, a wheel whole notches. Two-finger scrolling on a trackpad pans the graph, a mouse wheel
zooms it. This set checks that the editor switches between the two without a restart, which
matters on Wayland and Windows, where both devices arrive under one id. A right-button drag is
what hands scrolling back to the mouse; the trackpad takes over again by itself.

## Step: A mouse wheel zooms

**Action:**
Right after starting the editor, hover the Graph Window and turn the mouse wheel a few notches
in each direction.

**Expected:**
- The graph zooms in and out around the pointer.
- It does not pan.

## Step: Two-finger scrolling on the trackpad pans

**Action:**
Hover the Graph Window and scroll with two fingers on the trackpad, vertically, horizontally and
diagonally. End with a quick flick.

**Expected:**
- The graph pans in the direction of the fingers; it does not zoom.
- After the flick the view glides on for a moment and comes to rest.

## Step: A right-button drag hands control back to the mouse

**Action:**
Press the right mouse button on the Graph Window, drag the view a little, release, and turn the
wheel several notches in each direction.

**Expected:**
- The right drag pans the graph as usual.
- Every notch zooms the graph, in both directions; none of them pans.

## Step: The trackpad takes over again at once

**Action:**
After zooming with the wheel, scroll with two fingers on the trackpad.

**Expected:**
- The first swipe already pans the graph; no zoom step happens first.
- Pinching on the trackpad zooms (Linux and macOS; on Windows, Ctrl + two-finger scroll).

## Step: A fast trackpad swipe stays a pan

**Action:**
Swipe with two fingers as fast as you can several times in a row, pausing briefly between swipes.

**Expected:**
- Every swipe pans; none of them zooms.
