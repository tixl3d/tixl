---
id: creating-operators
title: Creating Operators
scope: graph-window
tags: [smoke, essential]
prerequisites:
  - An empty project is open
  - The Graph Window is visible
related-help:
  - ../.help/using/graph-window.md
---

Verifies the core flow of opening the Symbol Browser, finding an operator, and spawning it on the graph. Also a smoke test for the Parameter Window's selection-follows behavior.

## Step: Open the Symbol Browser

**Context:** On the Graph Window.
**Action:**
- Press `Tab`

**Expected:**
- The Symbol Browser opens.
- The search field is focused and empty.

## Step: Search for "RG"

**Context:** Symbol Browser is open.
**Action:**
- Type `RG`

**Expected:**
- The result list filters to operators matching the query.
- `[RadialGradient]` appears in the list.

## Step: Create a RadialGradient operator

**Context:** Search results are visible.
**Action:**
- Select `[RadialGradient]` (arrow keys + `Enter`, or click)

**Expected:**
- A `[RadialGradient]` operator is created on the graph at the cursor position.
- The new operator is selected.
- Its inputs are shown in the Parameter Window.
- The Symbol Browser closes.
