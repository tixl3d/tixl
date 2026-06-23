# Help Window (context-doc / tooltip panel)

[GitHub issue #102](https://github.com/tixl3d/tixl/issues/102). Originally requested (by *Sonik*)
as an **"explorer" feature for newcomers**: hovering a node shows a simple, to-the-point definition
in a dedicated spot so people can navigate the graph without breaking flow — in the spirit of FL
Studio's info pop / Ableton's help view. pixtur's design (see the
[Figma prototype](https://www.figma.com/proto/gtErVwBwDWla3GpiIwRf03/T3-UI-Lib?node-id=2771-490))
makes it a **dockable** panel that shows tooltips **without delay**, extended with operator
descriptions and — now — the cross-references from the documentation index (operator → meet-up
moments, examples). See [`../DOCUMENTATION_ECOSYSTEM.md`](../DOCUMENTATION_ECOSYSTEM.md) for the
index it reads. Start basic; expand the definitions over time.

## Modes

The panel has **two modes**:
- **Help** (default) — everything below.
- **Release notes** — the current version's changes; carries a **"has updates"** indicator when the
  user hasn't opened it since the latest version.

## States  *(within Help mode)*

- **Selected (◆)** — resting state: the doc of the currently-selected operator. Persistent, fully
  interactive (scroll / click).
- **Live preview (👁)** — while hovering, the panel **instantly** mirrors that item's doc, replacing
  the selected content. Targets (from #102): the hovered **operator**, **parameter**, **symbol in
  the Library / Symbol browser**, and any **UI-element tooltip**. **No dwell delay** — hovering must
  be instant so scrubbing a list (e.g. an op-composition's children) stays full speed.
- **Locked (🔒)** — a frozen preview; interactive. Hovering no longer changes it.

## The core interaction problem (settled)

Instant hover and a *docked* panel are incompatible with "move the mouse into the panel to read it":
the trip crosses other ops and swaps the content, and any delay that would mask the trip is the same
delay that kills exploration speed. Conclusion: **the lock trigger must not depend on mouse
position.**

- **A lock key freezes the current preview** and (optionally) moves keyboard focus to the panel — so
  you scroll (arrow / PageDown) and click links without the mouse ever leaving the graph.
  Flow: *hover to find → tap lock → read / click → `Esc` to resume.*
- **Click / select an op = the persistent pin** (Selected) — free, for selectable targets.
- **`Esc` or the close (✕) button dismisses a lock**, returning to the Selected/resting doc + live
  hover.
- Do **not** build a velocity / "safe-triangle" mouse heuristic — the key makes it unnecessary, and
  it's the finicky path.

Rejected alternative: freeze-the-content-when-the-pointer-enters-the-panel. Fails because you can't
reach a docked panel without hovering other ops on the way, and the dwell delay that would hide the
trip makes fast exploration sluggish.

## Affordances

- **State badge** in the header — `👁 live` / `🔒 locked` / `◆ selected` — so it's always clear *why*
  the content is what it is. Show the lock-key hint on the live state for discoverability.
- **Close (✕)** on the locked topic — mouse-accessible unlock (pairs with `Esc`); returns to Selected.

## Content (injected from the index)

On top of the existing description + parameter help (`OperatorHelp`):
- **"Discussed in meet-ups"** — from `mentions.json` (`op:` / `ui:` keys → timestamped deep links,
  each with its category + duration qualifier so the in-depth ones lead).
- Examples / related, when available.

Operators resolve by fullpath (`op:` keys); UI surfaces resolve via their `[HelpUiID]` (`ui:` keys).

## Open questions

- Exact lock-key binding (must be side-effect-free; `Esc` releases).
- Whether the lock always grabs keyboard focus, or only on a second tap.
- Does a locked non-operator topic (a `ui:` panel) coexist with a changing graph selection, or
  block it? (Lean: coexist — the lock is panel-local, independent of selection.)
