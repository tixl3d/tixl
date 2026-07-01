---
id: skill-quest-feedback
title: SkillQuest Feedback
added: 2026-05-25
added-in-version: 4.2
scope: skill-quest
tags: [user, essential]
prerequisites:
  - TiXL is open with no project loaded, or any project that is OK to leave running.
  - You have at least one snapshot defined on a Skills level (the default levels do).
related-help:
  - ../.agentic/Plans/Plan_SkillQuestFeedback.md
---

[ui:SkillQuest] gives you gentle, in-level guidance as you learn. A blue **Tip** icon
points you at the next parameter worth tweaking, and a yellow **Warning** icon
flags anything you've changed that isn't part of the solution. This walk-through
checks that those hints appear, move, and clear at the right times. Because the
level screen hides the main menu, the first step opens the helper windows you'll
need to follow along.

## Step: Toggling the Manual Test Runner during a level

**Action:**
From the Home screen, open the [ui:SkillMap|Skill Map], pick the **Gradients** topic and
start the first level. After the tour appears, press `Ctrl+Shift+Alt+T`.

**Expected:**
- The Manual Test Runner window appears on top of the level, even though
  the main menu is hidden.
- Pressing `Ctrl+Shift+Alt+T` again hides it.
- `Ctrl+Shift+Alt+L` shows and hides the Console Log window the same way.

## Step: Tour reading suppresses hints

**Action:**
Click through the tour points but stop on the first informational point
(its small dot label is muted, not orange). Wait at least 15 seconds
without clicking `Continue`.

**Expected:**
- No yellow Warning or blue Tip icons appear next to any parameter, on
  any operator badge in the graph, or anywhere in the [ui:ParameterWindow|Parameter Window].
- The output still renders normally — the tour itself isn't affected.

## Step: Hints fade in after the tour

**Action:**
Advance through the remaining tour points until the tour either reaches
its final "now you try" step or finishes completely. Wait without touching
any parameter.

**Expected:**
- After roughly 5 seconds, the first SkillQuest icons begin to fade in:
  a blue Tip icon on one parameter you should try, plus a yellow Warning
  icon on every parameter whose current value differs from the solution.
- After another 5 seconds the icons reach full strength.
- The matching badges on the operators in the graph fade in together with
  the parameter-row icons.

## Step: Only one Tip is visible at a time

**Action:**
Scan the Parameter Window and the graph badges for blue Tip icons.

**Expected:**
- Exactly one blue Tip icon is visible across the entire level — on a
  single parameter row and (if that parameter belongs to an operator
  visible in the graph) on that operator's badge.
- All other suggested parameters carry no icon.

## Step: Warning hint clears after revert

**Action:**
Pick any parameter that is currently showing a yellow Warning icon and
change its value (toggle a bool, drag a slider). Confirm the icon is
already visible. Then click the parameter's name button to revert it to
the default value.

**Expected:**
- The Warning icon disappears from the parameter row right away.
- The operator's badge in the graph updates at the same moment: if this was
  the operator's only flagged parameter, its yellow badge clears too.
- A new yellow icon does **not** reappear over the next 10 seconds — the
  parameter is back to its default.

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
- Right away, the blue Tip icon appears on a different suggested parameter
  (a checkbox, a dropdown, or a numeric value — whichever is the most
  useful next thing to try).
- The new Tip fades in over about 5 seconds rather than snapping straight
  to full strength.

## Step: Bool / enum tips have no progress arc

**Action:**
Keep solving the level until the focused Tip lands on a checkbox or
dropdown parameter (these tend to come up). Hover the icon.

**Expected:**
- The blue Tip icon is shown, but with no progress ring around it. A
  checkbox or dropdown is either right or wrong with nothing in between,
  so there's no "getting closer" arc to show.
- The hover tooltip starts with the colored hint line ("Try tweaking
  this parameter…" or "You're close — keep tweaking…").

## Step: Gradient parameters are silent

**Action:**
On a level that contains a `Gradient` input (the default Gradients
levels all do), inspect the gradient parameter row.

**Expected:**
- No Tip or Warning icon is ever shown on a gradient parameter row,
  even after editing the gradient.
- An operator that only differs from the solution in its gradient does
  not get a SkillQuest badge just because of the gradient.

## Step: Hover tooltip surfaces the hint text

**Action:**
Hover the parameter name of a parameter that has a SkillQuest icon (the
focused blue Tip, or any yellow Warning). Wait long enough for the tooltip.

**Expected:**
- The tooltip's first line is the SkillQuest hint message, in the icon's
  color (blue for a Tip, yellow for a Warning).
- The usual description and "Click to reset to default" notes follow
  below it.
- Other suggested parameters that aren't the current Tip show no hint
  message — just their normal description.
