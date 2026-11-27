# Smart Titles

**Status (2026-11-27):** phases 1, 2 and 4 built (uncommitted, on `main`); phase 3 partly: templates for [Multiply],
[Div], [Add], [Sub], [Text], [Blur]. Open: enum names, search matching, live visual check of the graph label.
Open questions answered with the proposals below.
**Layout changed after review (2026-11-27):** a smart title no longer replaces the label. The operator name stays the
main label and the smart title is a smaller second line below it (`DrawSmartTitle` in `MagGraphCanvas.DrawNode.cs`),
shown when the first row is tall enough (fades in from 6 px font size, `FontNormal` instead of `FontSmall` when zoomed
far in) and cut to the node width with `…`. Templates no longer need a prefix naming the op, so [Blur] uses
`Size {Size}`. Reviewer feedback: with the type and the content in one label they were hard to tell apart, and
the node space was poorly used.
`DescriptiveUi` is removed: operators implementing `IDescriptiveFilename` (AudioClip, VideoClip, LoadImage, LoadObj,
shader ops, ...) show their file name as the default smart title (`MagGraphItem.SmartTitleText`), so they share the
layout without templates in 17 `.t3ui` files. The legacy graph now shows only their name. `getGraphState` reports `smartTitle` per child for bridge tests.
Manual test set: [`graph-smart-titles`](../../.tests-manual/graph-smart-titles.md).

**Goal:** let a symbol define a title template that the graph shows instead of the symbol name, filled with
the child's current parameter values. Examples:

| Symbol      | Template            | Node shows         |
|-------------|---------------------|--------------------|
| [Multiply]  | `* {B}`             | `* 0.3`            |
| [Text]      | `{Text}`            | `TiXL loves you`   |
| [Blur]      | `Blur {Size}`       | `Blur 4`           |
| a user's `TitleClip` | `{Title}`  | `Intro`            |

Today users rename children by hand so the node tells them what's inside, and the name goes stale as soon as
they change the parameter. A smart title stays current. It's display-only: evaluation, the graph structure
and `SymbolChild.Name` don't change.

## Background

- `Editor/Gui/OpUis/DescriptiveUi.cs` already does this in code for ops that implement `IDescriptiveFilename`
  (34 ops). It draws the symbol name as a small title and the file name as the main value.
- `Editor/Gui/OpUis/UIs/` has ~40 hand-written custom UIs. Many of them only exist to show one value.
- The node label comes from `MagGraphItem.ReadableName` (`Editor/Gui/MagGraph/Model/MagGraphItem.cs:82`). It's
  drawn in `MagGraphCanvas.DrawNode.cs:238` and measured through `MagGraphItem.MeasuredLabel`, which only
  re-measures when the string changes.

## Precedence

1. Custom child name (`SymbolChild.HasCustomName`): shown in quotes, as today.
2. Smart title, when the symbol has a template and it resolves (see fallback below).
3. Symbol name.

A smart title is shown **without** quotes. The quotes then keep meaning "a person typed this", and a smart
title looks like an ordinary label. The symbol name stays reachable in the node's hover tooltip and the
parameter window header.

## Data model

On `SymbolUi` (`Editor/UiModel/SymbolUi.cs`, next to `Description` and `Links`):

```csharp
internal SmartTitle? SmartTitle { get; set; }   // null = no template
```

`SmartTitle` (new, `Editor/UiModel/SmartTitle.cs`) holds the template and its parsed form:

- `Template: string`: serialized form. Placeholders reference inputs by **Guid**:
  `* {9d5bc4bf-...}` or `{9d5bc4bf-...:0.00}`. Renaming an input then can't break it.
- `Segments`: parsed once on load or edit into literal text and `(inputId, format)` placeholders. It's
  never parsed per frame.
- Helpers `ToDisplayTemplate(Symbol)` / `TryFromDisplayTemplate(Symbol, string)` convert between the Guid form
  and the name form the user edits (`* {B}`). An unknown name on commit is kept as literal text and gets a
  warning in the edit field.

It stays in `Editor/` because the Player never draws graph labels (see "Keep `Core/` changes minimal").

### File format

A new optional top-level object in `.t3ui`, ignored by older editors:

```json
"SmartTitle": {
  "Template": "* {9d5bc4bf-1f71-4f3c-9a3a-1d0b4e3b2c11}"
}
```

It's an object, not a bare string, so later options (see interface audit) can go in without a migration.

## Resolving a title

For each placeholder, read the **child's stored input value** (`SymbolChild.Inputs[id].Value`), not the
evaluated slot value. That value changes only when someone edits it (or through undo, snapshots, variations),
never per frame. The label doesn't flicker and there's no per-frame formatting.

Fallback: if a referenced input is **connected or animated**, or no longer exists, the whole smart title
falls back to the symbol name. "`* ?`" or a stale value would be worse than the plain name.

Default formats by type (shorter than `ValueUtils.GetValueString`, which pads `0.300`):

| Type | Default | Notes |
|---|---|---|
| float | `0.###` | `0.3`, `12.5` |
| int | `G` | int inputs with a mapped enum show the enum name (phase 3) |
| bool | the input's name when true, empty when false | so `{Invert}` reads naturally |
| Vector2/3/4 | `0.##` per component, comma separated | |
| string | line breaks become spaces, cut at ~28 chars with `…` | the label's existing downscaling handles the rest |
| other | placeholder resolves empty | |

A placeholder format string overrides the default for numeric types (`{B:0.00}`). One named format is
reserved: `{Path:file}`, the file name of a path, so the `DescriptiveUi` behavior can be expressed later.

### Caching (no per-frame allocations)

A `SmartTitleCache` struct on `MagGraphItem` holds the resolved string and, per placeholder, a copy of the
last value seen (float/int/vector as a struct, string by reference with an `Equals` fallback). Each frame
it only compares the values; it rebuilds the string when one differs or the template instance changed.
Items with no template pay one null check. `MagGraphItem`s are rebuilt on layout changes, which just causes
one re-resolve.

I'd compare values directly instead of hooking `ChangeInputValueCommand`. Snapshots, variations, undo
and MIDI writes all change values, and direct comparison catches every path.

## Editing

1. **Edit Description dialog** (`Editor/Gui/Graph/Dialogs/EditSymbolDescriptionDialog.cs`): a single-line
   "Smart Title" field above the description, in name form, with:
   - an "Insert parameter" dropdown that adds `{Name}` at the cursor,
   - a preview line resolved with the default values,
   - a warning for unknown names.
   The undo command `ChangeSymbolDescriptionCommand` gets the original and new template, matching how it
   already snapshots description and links. `CommitEdits` compares the template too.
2. **"Use as Title" in the parameter context menu** (`Editor/UiModel/InputsAndTypes/InputValueUi.cs`, the
   `ContextMenuForItem` blocks at :201, :274, :406, :545). It sets the template to `{ThisInput}` and is shown
   checked when the template is exactly that. Unchecking it clears the template. It goes through the same
   undo command. This is the discoverable path; most users never need the dialog.

Both edit the **symbol**, so all instances change. The menu label and the dialog say so
("applies to all [Multiply]"). They're disabled for symbols in read-only packages, the same way the
description is.

## Phases

1. **Model and display**: `SmartTitle`, JSON read/write in `SymbolUiJson.cs` (key added to `JsonKeys`),
   resolving and caching, MagGraph label. The legacy graph (`GraphNode.cs:399`) stays as it is unless it
   still ships as a selectable view.
2. **Editing**: dialog field, context menu entry, undo. Duplicating a symbol gives every input a new Guid
   (`Editor/UiModel/Modification/Duplicate.cs:65`, `oldToNewIdMap`). Remap the template's placeholders
   through that map in `SymbolUi.CloneForNewSymbol` (`Duplicate.cs:96`), otherwise the copy's title falls back
   to its name. Check "Combine into new symbol" for the same id remapping.
3. **Content and polish**: templates for the obvious Lib ops ([Multiply], [Add], [Sub], [Text], [Blur],
   [Value]-like ops that don't already have a custom UI), enum names for int inputs, search dialog also
   matching smart-title text (`SearchDialog.cs:367` already matches custom names).
4. **Docs and tests**: `.help/` page section on naming operators, one bullet in
   `.help/release-notes/v4.4.md` (Other UI Improvements), manual test set `.tests-manual/graph-smart-titles.md`
   (`added: <date>`, `added-in-version: 4.4`) covering precedence, live update on edit, fallback on
   connect/animate, rename of an input, undo, save/reload.

## Interface audit (plausible next uses)

| Use case | Covered by |
|---|---|
| Per-child override ("show a smart title on this one Lib op") | optional child-level field later; doesn't change the symbol format |
| Two-line node (small symbol name + value, like `DescriptiveUi`) | optional `"Layout"` field in the `SmartTitle` object |
| Show output values (e.g. a [Value] result) | new placeholder kind, e.g. `{out:<guid>}`, inside the same string |
| Replace `DescriptiveUi` | `{Path:file}` format is reserved for that |
| Conditional text / enum names | format strings, no format change |
| Live values of animated inputs | opt-in flag in the object; would need the evaluated slot and per-frame caching |

Nothing in that list forces a rename or a change of shape.

## Open questions

1. **Style**: unquoted ordinary label (proposed), or a slightly different label color so smart titles are
   recognizable at a glance?
2. **Lib ops with no shipped template**: is a per-child override needed in v1, or are shipped templates plus
   custom names enough? Proposed: not in v1.
3. **"Operator" section at the bottom of the parameter window** (symbol description, help, smart title):
   useful, but it mixes symbol-wide settings into an instance view. Proposed: a follow-up, with a clear
   "applies to all instances" header if it happens.
4. Should `ReadableName` users outside the graph (undo labels, bookmarks toasts, connection tooltips) pick up
   the smart title? Proposed: no. Those mostly name the op type, and the template is a display aid.
