---
id: gpu-memory-budget-warning
title: GPU Memory Budget Warning
scope: app-bar
tags: [performance]
added: 2026-09-23
added-in-version: 4.3
prerequisites:
  - A project that renders something continuously (any graph with a Render Target).
  - A second application that can claim a lot of video memory — a design tool with a large
    document, or several browser windows with heavy pages.
---

Covers the app bar's warning for video memory being taken away by other applications. TiXL using
the whole card by itself is normal; being pushed past the budget the driver grants it is not, and
costs tens of milliseconds per frame in paging that otherwise looks like slow rendering.

## Step: No warning while TiXL has the card to itself

**Action:**
Close other GPU-heavy applications and open a project. Watch the app bar, right of the warnings
indicator.

**Expected:**
- No GPU memory warning, even when Task Manager shows dedicated GPU memory nearly full — a full
  card is not by itself a problem.

## Step: The warning appears when another application takes the memory

**Action:**
With TiXL still running, open the other application and give it enough to do to claim a large share
of video memory (watch Task Manager → Performance → GPU → Dedicated GPU memory). Wait a few seconds.

**Expected:**
- An orange warning icon with "GPU memory" appears in the app bar within about two seconds.
- Its tooltip names both numbers: what TiXL holds and what the driver currently grants it, the first
  being the larger, and explains that the difference is paged per frame.
- The Performance window's Present graph rises at the same time, which is where the paging is paid.

## Step: It clears again

**Action:**
Close the other application.

**Expected:**
- The warning disappears within a couple of seconds, without restarting TiXL.
- Frame and Present times return to what they were in the first step.
