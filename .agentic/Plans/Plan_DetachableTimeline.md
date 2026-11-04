# Plan: Detachable Timeline Window

## Goal

Let the timeline live in its own dockable window, shared by all graph windows (like the Parameter
window), while keeping the current "timeline under the graph" behaviour for anyone who doesn't opt in.

## Current state (2026-11-04)

- Each `ProjectView` owns its own `TimeLineCanvas` (`UiModel/ProjectHandling/ProjectView.cs:176`).
  So there is already one timeline *state* per graph window: zoom, scroll, mode, keyframe and clip selection.
- `GraphWindow.DrawContent` (`Gui/Graph/Window/GraphWindow.cs:205-276`) splits its own content area
  with `TimeLineCanvas.FoldingHeight.DrawSplit` and draws the timeline as a `##timeline` child below the
  graph when `UserSettings.Config.ShowTimeline` is set.
- `TimelineToolbar.Draw` (`Gui/Windows/TimeLine/TimelineToolbar.cs`) is drawn as an overlay at the bottom
  of the graph child and mixes three concerns:
  1. timeline fold toggle (custom height ↔ auto-fit height),
  2. transport (`TimeControls.DrawTimeControls`) and record button,
  3. graph image-background tools (`GraphImageBackground.DrawToolbarItems`).
- `TimelineToolbar.Draw` also runs the **global** per-frame work: `TimeControls.HandleTimeControlActions()`
  (all playback shortcuts) and `RecordingSession.OnFrame()`.
- `ShowTimeline` is toggled from: View menu (`AppMenuBar.cs:436`), Toggle All UI / focus mode
  (`UiConfig.cs:126,135,173`), the toolbar fold button, and the Animation/Live switch in
  `ProjectSettingsWindow.cs:1196-1210` (beat tapping hides the timeline).
- Layouts (`.Defaults/Layouts/layout*.json`) store only visible window titles plus the ImGui ini.
  `ShowTimeline` is **not** part of a layout.
- The timeline height is persisted per project on the root symbol (`TimelineState.TimelineHeight`).

### Latent bugs found while mapping this

- **Playback shortcuts fire once per graph window.** `HandleTimeControlActions` is called from each graph
  window's toolbar draw. Bindings use `ImGui.IsKeyPressed` with no dedup, so with two graph windows showing
  projects, `PlaybackToggle` (Space) toggles twice in one frame and playback appears not to start.
- Playback shortcuts and `RecordingSession.OnFrame` stop entirely when no graph window shows a project
  (e.g. all graph windows on the Hub).

## Options considered

**A. Timeline is always a window (full decoupling).** One code path, and `ShowTimeline`, `FoldingHeight`
and the `SplitFromBottom` height hack all go away. But:
- ImGui.NET 1.91.6.1 does not expose the internal `DockBuilder*` API, so existing layouts cannot be
  migrated by code. Every **user's** saved layouts (not just the shipped defaults) lose the timeline
  until they re-dock it by hand.
- Dock nodes have a fixed size, so the auto-fit height (grows with the number of animated parameters,
  capped at 200px) is lost.
- Live/beat-tapping mode hides the timeline today. With a window, that means either toggling window
  visibility (docked windows don't reliably return to their node) or leaving an empty dock node.
- The transport has to live somewhere when the timeline is hidden in Live mode, so the graph toolbar
  keeps a transport anyway.

**B. The visibility of the Timeline window decides (recommended).**
- Timeline window visible → timeline is detached: graph windows draw no embedded timeline, and the
  shared window draws `ProjectView.Focused.TimeLineCanvas`.
- Timeline window hidden → today's embedded behaviour, unchanged.
- No separate "docked to graph" flag. Layouts already persist window visibility, so each layout
  carries its own choice for free, and no existing layout breaks.
- Default layouts are only touched where we *want* a detached timeline (e.g. an "Animation" layout),
  re-saved from the editor rather than hand-edited ini.

The cleanup still happens in B: the timeline content gets one draw path used by both hosts, the toolbar
is split by concern, and the global per-frame work leaves the graph window.

## Behaviour spec (Option B)

| | Embedded (window hidden) | Detached (window visible) |
|---|---|---|
| Timeline body | below graph, splitter + fold toggle as today | fills `TimelineWindow`, size from dock |
| Which timeline | that graph window's `ProjectView` | `ProjectView.Focused` (switches with graph focus, like Parameters) |
| Transport row | graph bottom overlay | top row of `TimelineWindow`; graph has **no** bottom toolbar |
| Graph tool cluster (top right) | shown | shown |
| Fold toggle | shown | hidden (dock controls size) |
| View → Timeline (`ShowTimeline`) | shows/hides embedded timeline | shows/hides the timeline body; transport row stays |
| Beat-tapping / Live mode | hides embedded timeline (as today) | transport row with BPM/tap stays, body collapses with a short hint |
| No project focused (Hub) | n/a | empty-state hint |

Focus: clicking into the timeline window must **not** change `ProjectView.Focused` (it already doesn't,
because only `GraphWindow` calls `SetAsFocused`). Keyframe commands that resolve
`ProjectView.Focused?.TimeLineCanvas` (`DeleteKeyframeCommand`) keep working.

### Toolbar split (decided)

Today `TimelineToolbar.Draw` + `TimeControls.DrawTimeControls` draw one row. New ownership:

| Item (today, left→right) | Goes to |
|---|---|
| Fold toggle | Transport row, embedded only |
| Project Settings (`Icon.Settings`, emphasized if the op has settings) | **Graph tool cluster** |
| Current time / time display mode | Transport row |
| Idle motion beat grid, BPM edit, Sync/tap (beat tapping) | Transport row |
| IO indicator (`Icon.IO`, MIDI/OSC flash, data-set tooltip) | Transport row, right of idle motion |
| Jump start, prev/next keyframe, play back/forward, loop | Transport row |
| Curve/dope mode toggle | Transport row |
| Audio mute | Transport row |
| Hover preview mode | **Graph tool cluster** |
| Pin animated parameters | Transport row (acts on the dope sheet) |
| Image-background tools (`GraphImageBackground.DrawToolbarItems`) | **Graph tool cluster**, only while a background image is active |
| Record | Transport row |

The graph tool cluster is identical in both modes, so the graph window looks the same whether or not the
timeline is detached. Only the bottom overlay disappears. Use plain `IconButton`s without toolbar
backgrounds (the cluster floats on the canvas, it isn't a tiled bar), right-aligned at the top edge.

**Collision:** the minimap is drawn at the top right (`UiElements.DrawMiniMap`, `localPos.Y = 0`). Move it
below the cluster. Its `200×200` widget size is also unscaled; multiply by `T3Ui.UiScaleFactor` while
touching it.

## Implementation steps

1. **Move global per-frame work out of the toolbar.** Call `TimeControls.HandleTimeControlActions()` and
   `RecordingSession.OnFrame()` once per frame from the main UI loop, before windows draw. Also move the
   `UserActions.ToggleAnimationPinning.Triggered()` check out of the pin button's `if` (it has the same
   per-graph-window double-trigger and only fires while animated parameters exist). This can ship on its
   own. Verify that no user keymap binding for these actions relies on `NeedsWindowFocus`/`NeedsWindowHover`,
   because those context checks would then run outside any window.
2. **Split the toolbar.**
   - `TimelineTransport.Draw(ProjectView, bool showFoldToggle)` (rename/trim `TimelineToolbar`), drawing the
     transport items from the table above. `TimeControls.DrawTimeControls` loses the settings and
     hover-mode blocks.
   - `GraphToolCluster.Draw(ProjectView)` in `Gui/Graph/Window/`, drawing settings and hover mode, plus
     the image-background tools while a background image is active.
3. **Add `TimelineWindow : Window`** (`Gui/Windows/TimeLine/TimelineWindow.cs`), single instance, title
   `"Timeline"`, `NoScrollbar | NoScrollWithMouse`. `DrawContent`: resolve `ProjectView.Focused`, draw
   `TimelineTransport` as the top row, then the `##timeline` child calling
   `TimeLineCanvas.Draw(projectView, Playback.Current)`. Register it in `WindowManager._windows` so it
   appears in the Window menu and in layouts. Expose `internal static bool IsDetached`.
4. **`GraphWindow.DrawContent`:** gate `FoldingHeight.DrawSplit`, the `##timeline` child and the bottom
   transport overlay on `ShowTimeline && !TimelineWindow.IsDetached`. Always draw `GraphToolCluster`.
5. **`ShowTimeline` call sites:** keep the setting (body visibility). Add "Detach Timeline" /
   "Attach Timeline to Graph" next to the View → Timeline entry as the discoverable toggle for window
   visibility. The `ProjectSettingsWindow` Animation/Live switch keeps setting `ShowTimeline` only.
6. **`TimelineHeight`:** unchanged; only used when embedded.
7. **Edge cases to test:**
   - Two graph windows with different projects: the detached timeline follows focus, keeps each view's zoom
     and selection, and doesn't flicker when the Parameter window takes focus.
   - Focused graph window closed or switched to the Hub while detached: empty state, no null ref
     (`TimeLineCanvas.Draw` asserts `CompositionInstance != null`).
   - Focus mode (layout 11) without the Timeline window: falls back to embedded and hidden.
   - Project session restore (`ProjectView.cs:~505`) restores window visibility, so detached state is
     per-session as well as per-layout.
   - Drag-and-drop of clips/assets onto the timeline from another window (`TimelineClipDrop`).
   - Fence selection edge-scrolling and the inline curve/DataClip sub-canvases inside a floating window
     (multi-viewport).
   - `DataSetViewCanvas` external-canvas embedding still resolves `TimeLineCanvas` transforms.
8. **Docs/tests:** a `.tests-manual/` set for attach/detach and focus following, and a short `.help/`
   note on the Timeline window.

Default layouts: the maintainer re-saves them after this lands. Scaling layouts to other resolutions is a separate topic,
see `Plan_ScalableLayouts.md`.

## Open questions for the maintainer

1. Later follow-up: pin the timeline window to a specific graph window (like "lock to selection")? Not
   needed for the first version.
