# Help Window (context-doc / tooltip panel)

[GitHub issue #102](https://github.com/tixl3d/tixl/issues/102). Originally requested (by *Sonik*) as
an **"explorer" feature for newcomers**: hovering a node shows a simple, to-the-point definition in a
dedicated spot so people can navigate the graph without breaking flow — in the spirit of FL Studio's
info pop / Ableton's help view. pixtur's design makes it a **dockable** panel that shows tooltips
**without delay**, extended with operator descriptions and the cross-references from the documentation
index (operator → meet-up moments, examples). The index it reads is built by the pipeline in
[`../DOCUMENTATION_ECOSYSTEM.md`](../DOCUMENTATION_ECOSYSTEM.md). Start basic; expand over time.

This plan is implementation-ready: an independent session can build it from here. Sections 3 (model)
and 5 (indices) carry the decisions settled in design discussion.

## Status

**Phase 1 — implemented.** `Editor/Gui/Windows/HelpWindow.cs` (the shell: Help/Learn tabs, follow-selection
+ pin + history, body via `OperatorHelp.DrawHelp`), plus the helpers in `Editor/Gui/Help/`:
`HelpIndex` (loads + caches `mentions.json`/`videos.json` via `ShippedContent.ResolveDirectory(".help",
"references", "indices")`; the segment type is `OnlineVideoSegment`), `VideoResourceList` (ranked rows +
hover tooltip; the section heading adapts to the video types present), and `HoveredHelpTarget` (the
per-frame broker the graph feeds from `MagGraphCanvas.DrawNode`). Registered in `WindowManager`; indices
ship via a new `Editor.csproj` copy target. Manual test set: `.tests-manual/help-window.md`.

`VideoThumbnails` loads `<id>.jpg` from `.help/.tmp/video-thumbnails/` (WIC → texture → SRV, cached per
session) for the resource tooltip. **Shipping caveat:** that folder is git-ignored and not copied to the
build output, so thumbnails only resolve in a dev checkout. Shipping them in a release needs a tracked
location + copy target (or a pipeline step) — open.

Note for any window drawing an empty-state after `CustomComponents.SeparatorLine()`: render the body
inside a `BeginChild`/`EndChild`. `SeparatorLine` ends on a bare `SetCursorPosX` and `EmptyWindowMessage`
draws via the draw list (submits no item), so the dangling cursor otherwise trips ImGui's "validate
extent" assert at the window's `EndChild`. The child item validates the extent.

**Phase 2 — implemented (2026-07).** The pin/Shift model from §3–§4 was **dropped after review** in favor of
a context + hover-preview model:
- **Context = last explicit change wins.** Graph selection changes, Symbol-Library item clicks, `[ui:Id]`
  link clicks, and documentation-icon clicks each set the current help topic (`HelpWindow.ShowTopic`).
  Clicks on topic links/doc icons also open+focus the window if closed.
- **Hover preview toggle** (header icon, `UserSettings.Config.HelpHoverPreview`, default on): hovering an
  operator (graph, Symbol Library, Symbol Browser) or a `ui:` topic (markdown links, doc icons) previews
  its doc instantly and reverts to the context when the hover ends. While active, the Symbol Library /
  Symbol Browser description tooltips and `ui:`-link tooltips are omitted (no doubled content).
- **History**: every explicit context change is pushed with `NavigationHistory`-style dedup (newest at
  index 0, neighbor-selection keeps order); `‹`/`›` step through it, a new context jumps back out.
- **`ui:` topics render in the window** (`UiTopicDocs`: embedded snippet wins over `topics.json` doc) with
  their `ui:` mention segments in the resource footer; `HelpTopic` (op-Guid or ui-key) replaced the
  Guid-only follow target.
- **Symbol thumbnails** (`ThumbnailManager`, PackageMeta) show above the operator doc when available.

Decisions taken while building Phase 1 (still relevant):
- **No thumbnail image yet.** The `.jpg`s live in git-ignored `.help/.tmp/video-thumbnails/` (no release
  shipping story) and need string-keyed GPU-texture plumbing (`ThumbnailManager` is Guid/atlas-only). The
  hover tooltip carries the metadata header, title, date, and note instead. Top follow-up — see §7.
- **Learn tab wired to `ReleaseNotesLoader`** (the loader already existed). The "has updates" dot is still
  Phase 3.
- **Follow-hover sources**: the graph (`MagGraphCanvas.DrawNode`) and the Symbol Library
  (`SymbolLibrary.DrawSymbolItemInstance`). The Symbol Browser (`PlaceholderCreation`) is the remaining
  hover site — same one-line `HoveredHelpTarget.SetOperator(...)` when wired.
- The "Edit description…" button inside an empty doc only opens the dialog when the Parameter window is
  also visible (that window draws the shared `EditDescriptionDialog`). Minor; left to the Parameter window.

## 1. Modes

Two top-level modes, switched by a tab in the header:

- **Help** (default) — the contextual doc for the current operator / UI topic (sections below).
- **Learn / Release notes** — the current version's changes; carries a **"has updates"** indicator
  when the user hasn't opened it since the latest version. Content source TBD (release-notes file or
  the `update`-type videos in the index).

## 2. The panel body reuses existing code

The operator doc is already rendered by **`OperatorHelp.DrawHelp(symbolUi, isInTooltip)`**
(`Editor/Gui/Windows/OperatorHelp.cs`, a static renderer): title + namespace, the markdown
description with inline `[OpName]` links (`MarkdownView`), parameter details, and
`DocumentationRenderer.DrawLinksAndExamples`. **Call it — don't re-implement it.** The Help Window
adds the *shell* (window, modes, state machine) and the **"Discussed in meet-ups"** resource list on
top of that body.

## 3. Interaction model — pin + history (settled)

The hard constraint (from #102): instant hover and a *docked* panel are incompatible with "move the
mouse into the panel to read it" — the trip crosses other ops and swaps the content, and any dwell
delay that would mask the trip is the same delay that kills exploration speed. So **the freeze
trigger must not depend on mouse position** — it's a key (Shift), not a hover-into.

States:

- **Following selection** (default) — the panel instantly mirrors the hovered/selected op's doc. **No
  dwell delay** (scrubbing a list of children must stay full speed). Targets: hovered/selected
  **operator**, **parameter**, **symbol in the Library / Symbol browser**, and any **`[HelpUiID]`
  UI element**.
- **Pinned** — Shift **fixes** whatever's currently shown (hover or selection doc), **detaching** the
  panel from live selection and pushing that topic as the top/current entry of a **history stack**.
  The panel now stays put while you keep working in the graph. Fully interactive (scroll / click).

Navigation:

- **Pin** = `Shift` (toggle). Fixes the current topic and adds it to the history stack.
- **Back / forward** = `←` / `→` — step through the history stack; **landing on an entry pins it**
  (you're now fixed on that one).
- **Unpin** = `Shift` again, or click the pin icon / `✕` — reverts to *following selection*.

This resolves the earlier open questions: a pinned `ui:` topic **coexists** with a changing graph
selection (the pin is panel-local, detached); there is no selection-vs-arrows fight because pinning/
back-stepping is exactly what detaches from selection, and unpin re-attaches.

## 4. Affordances

- **Pin icon** in the header — toggles pin; shows a **"Press Shift"** discoverability hint that
  **retires after the first successful pin**. Pinned state reads as an active (filled) pin + a border.
- **`←` / `→` history arrows** in the header (tooltip: previous/next topic).
- **Mode tabs** (Help / Learn) in the header; the Learn tab shows the "has updates" dot.
- A **`✕` / unpin** affordance on a pinned topic (mouse-accessible unlock).
- Once several topics are pinned, a small **pinned-topics strip/dropdown** to jump straight to one
  beats cycling with the arrows (arrows stay good for the recent trail). *(nice-to-have)*

Follow the editor UI conventions: `UiColors.*`, every pixel literal `* T3Ui.UiScaleFactor`,
`CustomComponents`/`FormInputs`/`Icons` helpers, no per-frame allocations. The pin "on" state suits
the `CustomComponents.IconButton(..., ButtonStates.Activated)` filled look. MagGraph
(`Editor/Gui/MagGraph/Ui/MagGraphCanvas.DrawNode.cs`) is the style reference.

## 5. Content & the indices it reads

Built by the 3-stage pipeline (`video_to_srt.py` → `/analyze-videos` → `analysis_to_index.py`) into
`.help/references/indices/`. The window loads + caches these at runtime (decide the runtime path /
bundling — they live under the install's `.help/references/indices/`). Schemas:

**`mentions.json`** — the deep-link spine, keyed by `op:<fullpath>` or `ui:<id>`:
```json
"op:Lib.image.generate.noise.FractalNoise": [
  { "video": "a5_xCfC_3m4", "startSecond": 1620, "duration": 220,
    "url": "https://www.youtube.com/watch?v=a5_xCfC_3m4&t=1620s",
    "depth": "in-depth", "style": "experiment", "confidence": 85,
    "note": "How increasing resolution to 4K demonstrates fill-rate limits, and toggling Vsync off gives a cleaner ms measurement." }
]
```
Per segment: `startSecond` + `duration` (seconds — platform-agnostic, no human label). Three relevancy
axes — **`depth`** (`passing`/`explained`/`in-depth` = how much), **`style`**
(`scripted`/`answer`/`discussion`/`experiment` = how trustworthy; scripted > answer > discussion >
experiment), **`confidence`** (`0–100`). `note` is **user-facing**, written in an inviting
"what you'll learn" voice and may contain `[OpName]` links **for display** (the help UI's auto-linker
renders them — they are *not* mention markers). Overlapping segments of the same key+video are merged.

**`videos.json`** — `{ "videos": [ { id, type, date, title, url, duration } ] }`. `type` ∈
`meetup`/`tutorial`/`update`; `date` = YouTube upload date (recency). Thumbnails are saved per id at
`.help/.tmp/video-thumbnails/<id>.jpg` (id → thumbnail) for the tooltip.

**`topics.json`** — `{ "topics": { "ui:<id>": { term, parent, synonyms, classes, doc } } }`. The
hand-authored UI-topic registry (`.help/references/topics/ui-topics.md`), compiled. `classes` = the
implementing C# class(es); `doc` = the embedded help body (filled by the `/write-topic-docs` skill).

**Resolution.** `op:` keys are operator fullpaths → resolve to a `SymbolUi`. `ui:` keys resolve to
on-screen components via the **`[HelpUiID("<id>")]`** attribute (`Editor/Gui/HelpUiIDAttribute.cs`),
already applied this session to ~28 editor UI classes (windows, timeline areas, widgets). A future
generator can reflect those attributes into a `components.json` so `ui:Timeline` → highlight the
Timeline window.

## 6. "Discussed in meet-ups" — the resource list + relevancy

Below the doc body, list the topic's segments from `mentions.json`, **ranked**, top ~2 shown +
**"Show all N"**. Ranking = `depth × style(trust) × confidence × age`, with **operator usage-stats**
(how often the op is used across symbols) joined at query time as a tie-break/boost.

- **Age is weighted by type.** Heavier for `ui:` topics (the editor UI changed a lot across versions —
  a 4-year clip shows a window that no longer looks like that) and lighter for `op:` topics (the math
  is stable). Old resources get a quiet **"predates current UI"** cue rather than silently sitting on
  top. (TiXL was *Tooll3* >~2 years ago — a "4 years ago" tutorial is pre-rewrite.)
- Low-`confidence` or `experiment`-`style` segments can be **de-emphasized** vs `scripted`/`answer`.

## 7. Resource row + tooltip

- **Row** (compact): type + segment duration + age — e.g. `▶ Meet-up Example (5min, 1 year ago)`.
- **Hover tooltip** (no dwell): the **thumbnail** (`<id>.jpg`) with full video length badge (e.g.
  `5:23`), a metadata header surfacing the relevancy axes — **`1 MIN · IN-DEPTH · EXPERIMENT · YouTube`**
  (segment `duration` · `depth` · `style` · source) — the video **title + date**, and the **`note`**.
  Note the two durations are distinct and both useful: the **segment** length (`duration`) vs the
  **full video** length. Clicking opens `url` (`…&t=<startSecond>s`).

## 8. Implementation phases

1. **Phase 1** — Help mode, `op:` topics: a new `HelpWindow : Window` (register in `WindowManager` +
   `LayoutHandling`), follow-selection + pin + history, body via `OperatorHelp.DrawHelp`, the ranked
   meet-up resource list + tooltip. Real data exists for ops appearing in already-analyzed videos.
2. **Phase 2** — `ui:` topics: hover/resolve via `[HelpUiID]`, render `topics.json` `doc`, and
   (optionally) highlight the matched on-screen component.
3. **Phase 3** — the Learn / release-notes mode + "has updates" indicator.

## 9. Open / deferred

- ~~Exact `Shift` pin keybind~~ — obsolete; the pin model was replaced by context + hover preview (see Status).
- Where the editor reads the indices at runtime (install-relative `.help/` vs a bundled editor asset).
- The relevancy ranking weights (tune the age-by-type curve; the usage-stats source).
- The Learn/release-notes content source (a notes file vs the `update`-type videos).
- The pinned-topics strip/dropdown (§4) once multi-pin is in use.
