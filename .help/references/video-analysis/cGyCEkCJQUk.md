---
video: cGyCEkCJQUk
type: tutorial
date: 2022-09-09
title: Tooll 3 Tip#020 - Time Clips
duration: 0:04:11
focusesOn: [TimeClip], [ui:TimeOverrides]
---

A walkthrough of TimeClips: how clips on the timeline gate which parts of a graph get evaluated per scene, and how dragging or stretching a clip remaps the time fed into its sub-graph, with the colour cues (orange/red) that signal time remapping and time-scaling.

## Mentions
- 0:29→0:58 [TimeClip] · explained · scripted · Concept · 80% — How clips arranged on the timeline carve a single graph into separate scenes (intro, etc.) without needing sub-patches, each clip owning its own slice of the show.
- 0:58→1:18 [Layer2d] [TimeClip] · explained · scripted · Gotcha · 78% — Why an operator behind a clip stays unevaluated until playback enters that clip's span — content only "comes alive" while its clip is active.
- 1:18→1:48 [TimeClip] · explained · scripted · Example · 75% — How crossing from one clip into the next swaps which scene is being rendered, giving cut-style scene transitions purely from clip placement.
- 1:47→2:06 [ui:Timeline] · explained · scripted · Gotcha · 70% — Overlapping two clips on the timeline forces both scenes to evaluate at once, which raises render cost — visible as a frame-rate drop.
- 2:06→2:32 [ui:TimeOverrides] · in-depth · scripted · Concept · 82% — Clips tinted orange are remapping time: the clip overrides the time value its sub-graph receives rather than passing the global playhead through unchanged.
- 2:21→2:48 [ui:OutputWindow] · explained · scripted · Concept · 72% — Why a pinned output drives evaluation from its operator outward, and how a time-remapping clip in that chain substitutes the time used downstream.
- 2:48→3:09 [ui:TimeOverrides] [ui:AnimationArea] · in-depth · scripted · Example · 78% — Dragging a remapped clip slides its internal animation in time while it keeps reading the same underlying keyframes, letting you reposition a whole animated section without re-keying.
- 3:09→3:29 [ui:TimeOverrides] · explained · discussion · Gotcha · 70% — The catch with time-remapped clips: the original keyframes still exist underneath, so further tweaking becomes confusing even though the retiming is sometimes exactly what you want.
- 3:29→3:44 [ui:TimeOverrides] · in-depth · scripted · Tip · 80% — Holding Alt while dragging a clip turns it red and time-scales it (e.g. playing a section at 28% of its duration), speeding up or slowing down the whole clip.
- 3:44→4:00 [ui:TimeOverrides] · explained · scripted · Tip · 78% — How to undo a retime: right-click the clip and clear the time stretch to return it to 0% scale, then trim its start and end normally.
