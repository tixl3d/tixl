---
id: skill-quest-feedback
title: SkillQuest Feedback
added: 2026-05-25
added-in-version: 4.2
scope: skill-quest
tags: [essential]
prerequisites:
  - TiXL is open with no project loaded, or any project that is OK to leave running.
  - You have at least one snapshot defined on a Skills level (the default levels do).
related-help:
  - ../.agentic/Plans/Plan_SkillQuestFeedback.md
---

Walk-through for the in-level feedback that helps the user find the next
parameter to tweak (and warn them when they touched something that is not
part of the solution). The skill-quest layout hides the main menu, so the
first step is to confirm the new debug shortcuts work — without them the
rest of this set cannot be observed.

## Step: Toggling the Manual Test Runner during a level

**Action:**
From the Home screen, open the Skill Map, pick the **Gradients** topic and
start the first level. After the tour appears, press `Ctrl+Shift+Alt+T`.

**Expected:**
- The Manual Test Runner window appears on top of the level, even though
  the main menu is hidden.
- Pressing `Ctrl+Shift+Alt+T` again hides it.
- `Ctrl+Shift+Alt+L` toggles the Console Log window the same way.

## Step: Tour reading suppresses hints

**Action:**
Click through the tour points but stop on the first `Info` / `InfoFor`
point (the small dot label is muted, not orange). Wait at least 15
seconds without clicking `Continue`.

**Expected:**
- No yellow Warning or blue Tip icons appear next to any parameter, on
  any operator badge in the graph, or anywhere in the Parameter Window.
- The output renders normally — the tour itself is not affected.

## Step: Hints fade in after the tour

**Action:**
Advance through the remaining tour points until either the tour shows a
`CallToAction` step or the tour completes. Wait without touching any
parameter.

**Expected:**
- After roughly 5 seconds, the first SkillQuest icons begin to fade in:
  a Tip icon (blue) on one Required parameter, plus a Warning icon
  (yellow) on every parameter whose level value differs from the
  solution.
- After another 5 seconds the icons are at full opacity.
- The op badges on the graph fade in together with the matching
  parameter-row icons.

## Step: Only one Tip is visible at a time

**Action:**
Scan the Parameter Window and the graph badges for blue Tip icons.

**Expected:**
- Exactly one blue Tip icon is visible across the entire level — on a
  single parameter row and (if that parameter belongs to an op visible
  on the graph) on that op's badge.
- All other Required / Warm parameters carry no icon.

## Step: Forbidden hint clears after revert

**Action:**
Pick any parameter that is currently showing a yellow Warning icon and
change its value (toggle a bool, drag a slider). Confirm the icon is
already visible. Then click the parameter's name button to revert it to
the default value.

**Expected:**
- After the click, the Warning icon disappears from the parameter row
  on the next frame.
- The op badge updates the same frame: if this was the op's only
  Forbidden parameter, the yellow badge clears too.
- A new yellow icon does **not** fade in over the next 10 seconds — the
  parameter is back to default.

## Step: Tip stickiness while dragging

**Action:**
Find the focused Tip parameter (the one with the blue icon + progress
ring) and drag its value across the snapshot's target — i.e. all the
way past the correct value and out the other side, *without releasing
the mouse*.

**Expected:**
- The blue Tip icon and progress ring stay visible the whole time, even
  as the value momentarily passes through (or sits at) the correct
  value.
- The progress arc grows toward full as you approach the target value
  and shrinks again as you pass through it.
- Color shifts from dark toward white near the target.

## Step: Tip swaps to next parameter after release

**Action:**
With the focused Tip parameter selected, drag it to the exact correct
value and release the mouse.

**Expected:**
- The blue Tip icon disappears from the now-correct parameter.
- On the next frame, the blue Tip icon appears on a different Required
  or Warm parameter (a bool, an enum, or a numeric — whichever the
  relevancy ranking picks first).
- The new Tip fades in over ~5 seconds rather than snapping to full
  opacity.

## Step: Bool / enum tips have no progress arc

**Action:**
Keep solving the level until the focused Tip lands on a bool or enum
parameter (the relevancy ranking favours these). Hover the icon.

**Expected:**
- The blue Tip icon is drawn, but no progress arc / ring is rendered
  around it. Bool and enum proximity is binary, so the arc is
  intentionally suppressed.
- The hover tooltip starts with the colored hint line ("Try tweaking
  this parameter…" or "You're close — keep tweaking…").

## Step: Gradient parameters are silent

**Action:**
On a level that contains a `Gradient` input (the default Gradients
levels all do), inspect the gradient parameter row.

**Expected:**
- No Tip or Warning icon is ever drawn on a gradient parameter row,
  even after editing the gradient.
- The op holding only gradient differences also does not surface a
  SkillQuest badge purely on the gradient's account.

## Step: Hover tooltip surfaces the hint text

**Action:**
Hover the parameter name of a parameter that has a SkillQuest icon
(focused Tip or any Forbidden). Wait long enough for the tooltip.

**Expected:**
- The tooltip's first line is the SkillQuest hint message, drawn in the
  icon's color (blue for Tip, yellow for Forbidden).
- The standard description and "Click to reset to default" notes follow
  below.
- Non-focused Required / Warm parameters show no hint message — only
  their normal description.
