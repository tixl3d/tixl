# Using the timeline

Below each graph window is a timeline. Toggle its visibility in **Application Menu → View → Timeline**.

The graph's toolbar controls a few aspects of it:

- **Playback settings** — soundtrack, BPM, audio input.
- **Timeline format** — raster between bars, seconds, or frames.
- **Loop playback** — show the loop (work-area) handles.
- **Dope / Curve** — switch between the two primary editing modes.

## Controlling playback time

Scrub time by dragging in the lower shaded section of the timeline. (The upper area is for selecting keyframes and time clips — see [Selecting timeline elements](#selecting-timeline-elements).)

Playback shortcuts:

- `Space` — toggle playback.
- `Home` — jump to start.
- `L` — play forward; press again to speed up.
- `J` — play backward; press again to speed up.
- `K` — stop.
- `.` / `,` — jump to next / previous keyframe.
- `Shift + ←` / `Shift + →` — step 30 ms.

Hold `Shift` while scrubbing to snap to the bar raster and other timeline elements.

### Snapping and the time raster

In the background the **time raster** shows the current grid. Switch between:

- **Bars** — uses the project's BPM.
- **Seconds**.
- **F30** and **F60** frames.

Dragging timeline elements snaps to the bar raster by default. Zoom in for finer subdivisions (beats, ticks); zoom out for coarser ones (measures, phrases). Hold `Shift` to disable snapping. Raster density is configurable in **Settings**.

## Selecting timeline elements

Keyframes and time clips can be selected in a few ways. The selection drives both the **selection range indicator** (above the timeline) and the **keyset indicators** (the strip just below the ruler).

### Keyset indicators (the strip below the ruler)

Every visible keyframe — or cluster of nearby keyframes — is represented by a small dot in the strip directly below the ruler:

- **Unselected** — hollow dot.
- **Partially selected** — filled dot with a notch (some keys in that cluster are selected).
- **Fully selected** — filled dot.

Interactions:

- **Click a dot** — select the keys that dot represents, replacing the current selection.
- **Drag a dot** — move those keys through time without touching the broader selection.
- **Click on the background** (no dot under the cursor) — clear the keyframe selection.
- **Drag on the background** — fence-select. While you drag, the dots under the fence preview as selected. Hold `Shift` to add to the current selection, `Ctrl` to remove from it.

Clusters automatically merge when keys are within ~2 px on screen, so zooming in exposes individual dots and zooming out combines them.

### The selection range indicator

Inside the ruler there's a thin horizontal bar — the **selection range indicator (SRI)** — that spans the current selection:

- When **keyframes are selected**, the bar covers their time range (plus any selected clips).
- When **nothing is selected**, the bar dims to 30 % opacity and spans the extent of all visible keyframes, letting you grab everything at once without pre-selecting.

The bar has three hit regions, each lighting up independently when hovered:

- **Start handle** (left edge) — stretch from the right.
- **End handle** (right edge) — stretch from the left.
- **Middle line** — translate the whole selection.

**Stretching.** Drag either edge handle to scale the selected keyframes and clip endpoints proportionally around the opposite fixed boundary.

**Translating.** Drag the middle to shift everything. The translation snaps when the selection's start *or* end aligns with a snap target (raster, current time, clip boundary, etc.) — whichever gets a closer match wins. Hold `Shift` to bypass snapping.

If nothing is selected, an SRI drag auto-selects every visible keyframe first, so you can simply grab the bar and stretch all keys of the visible parameters together.

### TimeWarp handles

For retiming a section of the selection non-uniformly, the SRI supports **TimeWarp handles** — small filled circles placed on the bar.

- **`Alt + Click`** on the bar inserts a handle at the cursor.
- **`Alt + Click`** on an existing handle removes it.
- **Drag a handle** (with or without `Alt`) to retime keys and clip endpoints in the two adjacent segments. The previous handle (or the SRI start) and the next handle (or the SRI end) stay pinned; everything between them is remapped piecewise-linearly around the moving handle.
- With handles present, **dragging the SRI start or end** retimes only the outermost segment instead of stretching the whole selection.
- **Middle-translating** the SRI shifts the handles along with the keys, so they stay visually attached.

Handles are cleared automatically when you replace the keyframe selection (clicking a different keyset indicator, fence-replacing, or deselecting everything). Adding to or removing from the existing selection leaves them in place. Handles outside the current selection range are hidden.

Handle drags are undoable — `Ctrl + Z` restores both the keyframes and the handle positions. Inserting and removing handles themselves is not part of the undo history.

While `Alt` is held, hovering the bar previews an outlined circle at the cursor — solid outline means "insert here", centered on an existing handle means "remove this one".

### Scrub, no-drag, and the legacy range

Directly under the ruler you may also see the older **time-selection range** (a translucent band with handles at the bottom of the canvas) while holding `Alt` over the dope sheet. It has the same stretching behavior as the SRI and is kept for muscle-memory compatibility — the SRI is the recommended workflow.

## Working with the Dope Sheet

The dope sheet displays the keyframes of the currently selected operators.

### Adding and editing keyframes

To animate a parameter, click its input in the *Parameter Window* while holding `Alt` to insert the first keyframe:

![Inserting the first keyframe](/images/Animation/anim-8.gif)

Once animated, you can:

1. **Insert new keyframes** by modifying the parameter value.
2. **Toggle keyframes** via the keyframe indicator in the parameter window.
3. **Adjust interpolation** via the context menu.
4. **Adjust a keyframe's value** by selecting it and editing in the parameter window.
5. **Insert keyframes on a curve** by `Alt`-clicking the animation curve directly.

To **remove an animation** either delete all its keyframes or reset the parameter by clicking its name in the parameter window.

### Displaying keyframes

By default, only the keyframes of selected operators appear in the timeline. Enable **Keep animated parameters visible** in the toolbar to keep parameters pinned even when you select other operators:

![Keep animated parameters visible](/images/Animation/image-9.png)

If too many parameters stack up, press `Ctrl + Shift + K` twice to clear the list.

### Interpolation and tangents

The keyframe context menu switches between interpolation modes. For fine control of tangents, flip the mode toggle from **Dope Sheet** to **Curve** and edit the curve directly.

### Inserting animation increments

Press `Shift + C` to insert a keyframe whose value is incremented by 1 relative to the previous one. This is useful for tapping **step markers** while a soundtrack plays:

1. Add a soundtrack to your project.
2. Press `Home` to jump to the start.
3. Create a `[Value]` operator and animate it. Set its interpolation to **Constant**.
4. Press `Space` to play.
5. Press `Shift + C` on every beat you want to mark. Keyframes with values `1, 2, 3, …` appear at those times.
6. Feed the animated value into `[HasValueChanged]` + `[TriggerAnim]` (or similar) to drive events.

To recompute the keyframe values after inserting or deleting keys:

1. `Shift + Click` the keyframe row to select all keyframes.
2. Run **Recount values** from the context menu.

## Working with time clips

Some operators — notably `[TimeClip]` and `[VideoClip]` — output time clips. They're evaluated only while the current playback time falls within their clip time; outside that window they're skipped, as if disabled.

If a composition contains any time clips, the timeline shows a **Time Clip Area** above the dope sheet. Combine clips with a `[Group]` to build a scene timeline:

![Time clips in a group](/images/Animation/anim-4.gif)

The active clip is highlighted during playback.

### Time clips need to be connected

> [!NOTE]
> Clips not connected to a consumer never render unless they're pinned to the output window. TiXL fades unconnected clips and shows a "Not Connected?" warning on their tooltip:

![Inactive clip warning](/images/Animation/image-7.png)

### Editing time clips

Drag one or more clips with the left mouse button. They snap to:

- The timeline raster (in **Bars** mode).
- Other time clips.
- The current playback time.
- Other keyframes.
- The loop range (when active).

Hold `Shift` to disable snapping. Rename a clip by pressing `Return`.

### Time clip layers

Drag clips vertically to distribute them across layers. Layers **do not** affect rendering order — connection order in the group does. TiXL distributes overlapping clips into separate layers automatically; `Ctrl + Shift` while dragging vertically adjusts only the layer index.

### Time remapping

Time remapping lets a clip scale or offset the source time it exposes to its sub-graph — similar to `[SetCommandTime]`, but local to the clip.

![Remapped time clip](/images/Animation/anim-6.gif)

In the example above, the second clip plays its contents at 500 % speed. The red bar at the bottom of the clip indicates the remap.

Selecting a single clip shows two curves visualizing the mapping from clip time to source time:

![Time remap curves](/images/Animation/image-8.png)

Ways to edit the remap:

1. Drag a clip's start or end while holding `Alt`.
2. **Slip-drag** the source region by dragging inside the clip with `Alt + Ctrl`.
3. Select clips and choose **Edit Clip Times** from the context menu.
4. **Clear Time Stretch** from the same menu resets the mapping.

> [!NOTE]
> Remapped time interacts with keyframe editing in sometimes-surprising ways. Consider whether the remap is actually necessary before committing to it.

### Splitting clips

Select one or more clips and run **Cut at Time** from the context menu. Each clip splits at the current playback time; the new clip is renamed and reconnected to the same target group.

### Nesting timing

If an operator symbol has an output flagged as **time clip**, instances of that operator also appear in the time clip area. Unlike `[TimeClip]`, nested time-clip operators do **not** link their source time to the clip time — moving such an operator from `t=0` to `t=1` bar leaves its source time at `0`.

### Editing videos

The clip workflow doubles as a lightweight video editor for `[VideoClip]`s. Seeking inside heavily-compressed sources is slow; if you plan to scrub a video, re-encode it first with an all-intra preset:

```bash
ffmpeg -i input.mp4 -g 1 -c:v libx264 -preset veryfast -crf 18 output.mp4
```

### Clip Timing Editor

Open the **Clip Timing Editor** to set precise times for one or more selected clips:

![Clip timing editor](/images/Animation/anim-10.gif)

When a selection contains multiple values, the editor shows `NaN` for the mismatched field — type a number to set it across all selected clips at once.
