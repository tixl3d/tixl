---
id: search-dialog
title: Search Dialog (Ctrl+F)
scope: graph-window
tags: [essential, user]
added: 2026-07-07
added-in-version: 4.2
prerequisites:
  - A project with a user Symbol that has at least one named input and one output is open.
---

Covers the Ctrl+F search dialog: finding operators, and jumping to the Input and
Output nodes of the current Symbol.

## Step: Opening the search dialog

**Action:**
With the Graph Window focused, press `Ctrl+F`.

**Expected:**
- The Search dialog opens with its text field focused.
- With an empty search field, recently visited operators are listed as history.

## Step: Finding an operator by name

**Action:**
Type part of the name of an operator that exists in the current composition.

**Expected:**
- Matching operators appear in the result list.
- Clicking a result (or highlighting it with the cursor keys) centers and selects that operator in the graph.

## Step: Finding an Input node

**Action:**
Enter a Symbol with named inputs (e.g. double-click a user operator), press `Ctrl+F`, and type part of an input's name.

**Expected:**
- The input appears in the result list, labeled "(Input)", listed before matching operators.
- Activating the result scrolls the graph to the Input node and selects it.

## Step: Finding an Output node

**Action:**
With the search dialog still open, clear the field and type part of an output's name (e.g. `Output`).

**Expected:**
- The output appears in the result list, labeled "(Output)".
- Activating the result scrolls the graph to the Output node and selects it.

## Step: Finding inputs in child Symbols

**Action:**
Navigate to the parent composition, press `Ctrl+F`, switch the dropdown to `LocalAndInChildren`, and type the name of an input defined inside a child Symbol.

**Expected:**
- The child Symbol's input appears in the result list, labeled "(Input)".
- Activating it enters that child Symbol and focuses its Input node.
