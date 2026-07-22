# Output Setup — Row States

The per-row visual states for the sidebar tree, across all four row types (Image / Slice / Surface / Output).

![Output setup panel node states](images/2026-07-21%20output%20setup%20panel%20node%20states.png)

*Source of truth: the Figma state matrix above. The token table below is the implementation reading of it.*

Row **anatomy** (what varies by type) is fixed by the gutter grammar (see spec): left gutter = content-*in*,
right gutter = *out*, each shown only at the level that owns it.
- **Image / Slice** — pure sources: **out-gutter only** (`→⊞`).
- **Surface** — midpoint: **in *and* out** (`[slice]→ … →🎥`).
- **Output** — pure sink: **in-gutter only** (`⊞→`) + a trailing **device label** (`Display1`).

Read the gutters top-to-bottom and the pipeline reads `source → surface → sink`.

## State tokens (proposed)

Row-level treatment, independent of row type. Colors are `UiColors.*` semantics; exact fades are starting
points to tune against the matrix, not pixel-law.

| State | Background | Outline | Text / icons | Gutter glyphs | Trigger |
|---|---|---|---|---|---|
| **Default** | none | none | `Fg` | dim (`Fade ~0.3`) | resting |
| **Selected** (primary) | `Active` solid fill | none | `FgFull` (bright) | visible | this row is selection primary (index 0) |
| **Additionally Selected** (member) | `Active.Fade(~0.4)` fill | subtle | `Fg` | visible | shift-added multi-select member; the drag payload |
| **Hovered** | neutral `White.Fade(~0.08)` fill | none | `Fg` | dim | pointer over row |
| **Referenced** | `Active.Fade(~0.1)` fill | `Active.Fade(~0.4)` 1px | `Fg` | **lit** (`Active`) | another row (a consumer) is hovered/selected and references this one |
| **Dragged** | dark/black solid pill | none | `FgFull` | **hidden** (icon + name only) | the floating chip under the cursor mid-drag |
| **Drop-Target** | faint | **warning/green** 1px outline | `Fg` | dim | valid drop while dragging |
| **Unbound** | none | none | `Fg` | **out-glyph → `⚠️`** (`StatusError`) | target/display no longer resolves |
| **Unused** | none | none | `Fade(~0.4)` | `Fade(~0.4)` | referenced by nothing (spec callout 7) |

Precedence when in-tree states combine: **Selected > Drop-Target > Referenced > Hovered > Default**; **Unbound**
and **Unused** are orthogonal decorations (an *Unbound* row can still be *Selected*). *Additionally Selected* is
the non-primary form of *Selected*. **Dragged** is not an in-tree row state — it's a **floating overlay chip**
following the cursor, outside the precedence chain; the origin row stays in the tree (typically dimmed while
its chip is aloft).

## Notes & the one gap left

- **Referenced is induced, not intrinsic** — it's the cross-highlight a row shows because *another* row (a
  consumer: an Image, a send, an Output) is hovered/selected. Hover-induced and selection-induced look the
  same; hover is transient, selection sticky.
- **Unbound vs Unused are different** — *Unused* = "nothing points here" (faded, benign); *Unbound* = "a link
  that should resolve doesn't" (a red `⚠️` in the out-gutter — error, needs fixing). The matrix now
  distinguishes them; don't collapse them into one grey.
- **Dragged is a floating overlay, not a tree row** — the dark chip (icon + name, no gutters) follows the
  cursor; the origin row stays put in the tree (dim it while the chip is aloft). For a **multi-drag**, show a
  single stacked chip with a count rather than N chips. It reads the current *Additionally-Selected* set as its
  payload and *Drop-Target* as its destination (spec callout 21).

## Mapping to code
- These states are rendered by the reusable **tree-row component** (`data-model.md` §3 — to be factored out of
  `SetupPanel`). It takes the row's `SelectionTarget` state (from the two planes in [`selection.md`](selection.md))
  + reference/hover/bind status and picks a row above.
- *Referenced* consumes the hover/selection cross-highlight rule; *Drop-Target* consumes the drag-drop hit-test;
  *Unbound* consumes target/`DeviceBinding` resolution.
