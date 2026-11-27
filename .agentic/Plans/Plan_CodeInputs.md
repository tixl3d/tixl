# Code inputs: annotated parameters and syntax highlighting

Status: **Phase 1 done** (2026-11-21), **Phase 2 built** (2026-11-27), plus error display and the code editor popup ahead of Phase 4 (2026-11-22,
built and unit-tested, not yet tried in the editor). Drafted 2026-11-20.

## Goal

Ops that take a code snippet (the Custom* shader ops, `SelectGeometry`, the `*FromSource` ops) expose
generic inputs `A`, `B`, `C`, `D`, `Offset`. What they mean depends entirely on the snippet, so a preset is
hard to use without reading its code. Two improvements, sharing one foundation:

1. **Parameter annotations.** A comment in the snippet relabels an input and sets its slider range:
   `// A: Scale 0..1`. The parameter window shows "Scale" with a 0…1 slider for *this instance*.
2. **Syntax highlighting** (and later error lines and autocomplete) for the code itself.

Both hang off a new string input usage, `StringInputUi.UsageType.Code`, with a separate `CodeLanguage`
setting. Annotations travel with the code string, so presets arrive already labelled.

## What exists today

- `StringInputUi.UsageType` (`Editor/Gui/InputUi/SimpleInputUis/StringInputUi.cs:24`) has `Default`,
  `FilePath`, `DirectoryPath`, `Multiline`, `CustomDropdown`, `OutputFilePath`, `MidiDevice`. The code inputs
  of all snippet ops use `Multiline`, drawn by a plain `ImGui.InputTextMultiline` (`StringInputUi.cs:169`).
  One text colour, no highlighting. The editor has no highlighting code anywhere.
- `Usage` and `FileFilter` serialize as optional fields in `.t3ui` (`StringInputUi.Write/Read`, `:260-283`);
  a new optional `CodeLanguage` field fits that pattern.
- Parameter labels are drawn from `input.Name` in `InputValueUi.DrawParameterEdit`
  (`Editor/UiModel/InputsAndTypes/InputValueUi.cs:264, 405, 514`, one per state: connected, animated, plain).
  The description tooltip is `DrawInputTooltipAndResetIcon` (`:721`).
- Float ranges are symbol-level `InputUi` properties (`Min`, `Max`, `Scale`, `ClampMin`, `ClampMax` in
  `FloatVectorInputValueUi.cs:326`), passed to `SingleValueEdit.Draw` / `VectorValueEdit.Draw`
  (`FloatInputUi.cs:21, 31`). They are per *symbol*; the annotations are per *instance*.
- Precedent for a per-draw override: `InputArea.DimHighlightOverride` and `ValueEditRightMargin`
  (`InputValueUi.cs:774-780`) are static fields set around `DrawParameterEdit` and reset afterwards.
- C# snippets: `Operators/Lib/Utils/CSharpSnippetCompiler.cs` already places the snippet after `#line 1`, so
  errors report snippet-relative lines (`"line N: message"`). HLSL snippets are spliced into a template file
  (e.g. `Lib:shaders/points/modify/CustomPointShader-template.hlsl`); line mapping is not traced yet.

### Ops in scope

| Op | Language | Annotatable inputs |
|---|---|---|
| `CustomPointShader`, `CustomPixelShader`, `CustomForce`, `CustomVertexShader`, `CustomFaceShader`, `CustomDrawMesh`, `CustomSDF` | HLSL | A–D, `Offset`, others |
| `CustomScalarField` | C# | A, B |
| `SelectGeometry` (new) | C# | A, B, `Offset` |
| `CustomGeometryAttribute` (new) | C# | A, B |
| `ComputeShaderFromSource`, `PixelShaderFromSource` | HLSL (full shader) | none; highlighting only |
| `Expression` | one-line formula | A–D, but **no comment syntax**, see open decision 3 |

## Annotation syntax

One annotation per line, anywhere in the snippet:

```hlsl
// A: Scale 0..1                 label + soft slider range (values may go beyond it)
// B: Twist [-180..180]          label + clamped range
// C: Seed                       label only
// Offset: Center                works for any input name, not only A-D
// D: Falloff 0..2 - distance where the effect fades out
                                 trailing " - text" becomes the tooltip
```

- Grammar: `// <InputName>: <Label> [<range>] [- <tooltip>]`, with `<range>` being `min..max` (soft) or
  `[min..max]` (clamped). `<InputName>` matches the symbol's input name case-insensitively; the label may
  contain spaces.
- **Soft by default.** Clamping is opt-in, in line with how TiXL treats blend and lerp factors
  (extrapolation is a feature). Square brackets are the familiar notation for a closed interval and read
  better than the `{{0..1}}` first floated.
- Lines that don't match are ignored silently: it's a comment, and half-typed annotations must not produce
  warnings while the user types.
- The original name stays visible: the label row shows `Scale` and the tooltip begins with `A`, because the
  code still refers to `A`.
- Ranges apply to float/int inputs and to every component of vector inputs. For other types only the label
  and tooltip apply.

## Architecture

- **`UsageType.Code` + `CodeLanguage`.** Add `Code` to `StringInputUi.UsageType` and an optional
  `CodeLanguage` string property (`"Hlsl"`, `"CSharp"`, …; a string, not an enum, so new languages need no
  format change). `Code` draws exactly like `Multiline` until the editor widget lands. Unknown or empty
  language = no highlighting. Editable in `StringInputUi.DrawSettings` like `FileFilter`.
- **Parser: `CodeParameterAnnotations`** (Editor, new file next to `StringInputUi`). Parses a code string
  into a small list of `{ InputName, Label, Min, Max, HasRange, Clamp, Tooltip }`. Pure function, no ImGui.
  Language-agnostic for every language with `//` comments.
- **Per-instance lookup with no per-frame allocation.** In `DrawParameterEdit`, resolve the annotations for
  `inputSlot.Parent`:
  - find the symbol's first input whose `StringInputUi.Usage == Code` (cache the index per `SymbolUi`, which
    is invalidated on symbol reload);
  - read that slot's current *evaluated* value, so annotations still work when the code comes from an
    upstream `String` op;
  - look the parsed result up in a `ConditionalWeakTable<string, CodeParameterAnnotations>`. Strings are
    immutable, so a new reference means new content, and entries are collected with their strings. It is
    re-parsed only when the code changes.
- **Applying the override.** Label and tooltip: replace `input.Name` at the three label sites with a
  resolved display name. Range: a per-draw override (same pattern as `InputArea.DimHighlightOverride`) that
  `FloatInputUi` / int / vector UIs read in `DrawEditControl` instead of their symbol-level `Min/Max/Clamp`.
  Set it before `DrawEditControl` and reset it after. Nothing is written to the symbol, `.t3ui` or undo
  stack; display only.
- **Ownership: Editor only.** No `Core` change. The Player never sees annotations.

## Phases

### Phase 1: `Code` usage (no visible change)
- **Done 2026-11-21.** `UsageType.Code` and `CodeLanguage` (serialize, clone, settings); the code inputs of the ops in
  scope (both inputs where an op has `ShaderCode` and `AdditionalDefines`) use it, `CustomScalarField`, `SelectGeometry`
  and `CustomGeometryAttribute` with `CSharp`, the rest with `Hlsl`. `DrawMesh.ShaderDefines` stays `Multiline`.
- Add `UsageType.Code` and `CodeLanguage` (serialize, clone, settings UI).
- Switch the ops in scope from `Multiline` to `Code` with their language in `.t3ui`.
- Done when: all snippet ops load, edit and save as before; `.t3ui` diff is only the usage/language fields.

### Phase 2: Parameter annotations (built 2026-11-27, not yet tried in the editor)
Done: `Editor/Gui/InputUi/SimpleInputUis/CodeParameterAnnotations` parses the code (cached per string in a
`ConditionalWeakTable`, resolved once per op and frame). `InputValueUi.DrawParameterEdit` uses the label for all three
name buttons as "Label (A)", shows the annotation's tooltip, labels inputs marked unused with `// C,D: -` as "- (C)"
(decision 1, changed twice on 2026-11-27: dimming every unlabelled single-letter input also dimmed inputs the code used,
and dimming marked ones hid whether they were at their default; several names may share any line), and sets `InputArea.RangeOverride` for the row; the numeric input UIs read their
range through `GetDrawRange`. Syntax as recommended (decision 4). Parser checked against the edge cases by hand; there
is no Editor unit-test project. Still open: popover labels, node labels, and annotating the bundled presets (none use
the convention yet; the user starts by hand, then infer more from the code).

- `CodeParameterAnnotations` parser + unit tests for the grammar (edge cases: missing range, reversed
  range, negative/decimal numbers, unknown input name, duplicate annotation = first wins).
- Lookup + override in `DrawParameterEdit` and the float/int/vector edit controls.
- Also apply labels in `ParameterPopover` (`Editor/Gui/MagGraph/Interaction/KeyboardEditing/`) so the
  keyboard editing path matches the window.
- Annotate the bundled presets and examples of the Custom* ops (largest user-visible win).
- Docs: annotation section in `.help/docs/advanced/UsingCustomShaders.md`; release-notes bullet; manual
  test set `.tests-manual/code-parameter-annotations.md`.

### Phase 3: Highlighted code view
- Highlighter: small tokenizer per language (comments, strings, numbers, keywords, types, built-in helpers)
  producing colour runs; cached per string like the annotations. Colours come from new `UiColors` entries.
- Widget, recommended first step: **highlighted read-only view, edit on click** (option B below). Robust and
  cheap; the colours show while reading, not while typing.
- Annotation comments get their own colour so they visibly read as configuration.

### Error display (done 2026-11-21, ahead of Phase 4)
- `Core/Operator/Interfaces/IInputStatusProvider` lets an op report a problem with one input as
  `InputStatus(Message, Line)`, Line 1-based in the input's text or 0. This is the one Core piece of this plan: the
  error comes from the running op in Lib, which cannot see the Editor.
- `Code` inputs ask the selected op for it and draw an error border plus the message in small text below the field.
- `SelectGeometry` and `CustomGeometryAttribute` report compile errors (with `CSharpSnippetCompiler.FirstErrorLine`)
  and missing `@Name` attributes, listing the available ones.
- HLSL ops need no code of their own: `Editor/Gui/InputUi/SimpleInputUis/CodeInputStatus` falls back to a child
  `IShaderCodeOperator` (up to two levels down) with a warning, finds each `Code` input's text in the child's spliced
  source and maps the compiler's `Line N` into the input it falls in (shown there, prefix and file name dropped,
  hint kept); otherwise the raw message goes below the first `Code` input. Verified on CustomPointShader (ShaderCode,
  AdditionalDefines) and CustomPixelShader. `getGraphState` reports it per child as `codeInputStatus`.
- Phase 4 underlines `Line` instead of only showing the message. The available `@Name` attributes are the first
  op-provided symbol list for autocomplete.

### Code editor popup (built 2026-11-22, not yet tried in the editor)
- `Editor/Gui/InputUi/SimpleInputUis/CodeEditorPopup`, opened by an icon at the right end of a `Code` input's name row
  (red while the code has an error). Like `GradientEditorPopup`: move grip, resize grip (both now
  `CustomComponents.DrawPopupMoveGrip` / `DrawPopupResizeGrip`), Escape closes.
- Line numbers without a real editor widget: a gutter and an `InputTextMultiline` sized to its whole text share one
  scrolling child, so the monospace code font puts line N at a fixed y. Problem lines get a tint drawn under the
  transparent field; a text callback tracks the cursor line so the child follows it while typing, and places the
  cursor when a problem is clicked.
- Escape would revert the whole edit in an ImGui text field, so the popup claims the key every frame
  (`DrawUtils.ClaimEscapeFromTextInputs`) and closes itself, keeping the text. Closing reports `Finished` so the edit
  lands in undo.
- **Compiled source** toggle: the child shader op's spliced source, read-only, with its own line numbers and the
  snippet tinted. This is the toggle between snippet and final line numbers; no `#line` markers are needed while
  templates paste the snippet unchanged, since `CodeInputStatus` finds it in the source.
- Footer lists every problem. Core keeps them now: both compilers fill a list of
  `ShaderCompileFailures.Diagnostic(IsError, File, Line, Column, Text)` (File empty for the compiled source itself),
  stored on the `Failure` together with the source; `ShaderCompileFailures.TryFindBySource` finds them, and
  `getShaderErrors` returns them as `diagnostics`. Problems in the snippet show as `Line N`; others as
  `Template line N` (click switches to the compiled source) or `file:N` for includes. C# ops without that list show
  their one `InputStatus`.
- Fixes in `CodeInputStatus`: an error in an included file is no longer mapped into the snippet, and an input whose text
  occurs more than once in the source (e.g. a short define) is not mapped. DX11 messages no longer carry the
  column in the line number (`Line 40,5-12:`, which the mapping couldn't read); the D3D compiler is now given the
  shader's name as source file name so its own lines can be told from includes (`FxcDiagnostics`, untested on
  Windows).
- Known limits: an ImGui popup closes on a click outside, so sliders can't be dragged while it is open (a dockable
  window would solve that); text over 32000 characters is cut, as in the inline field.

### Phase 4: Real code editor (separate decision)
- Inline highlighting while typing, error underlines, autocomplete. Options in "Widget options".
- Error lines: done for the message (see Error display); Phase 4 adds underlines in the editor widget.
- Autocomplete: each op provides its symbol list (`p`, `idx`, `f`, `SampleGradient`, the annotated A–D
  labels). Shape of that registry is open.

## Widget options

| Option | Effort | Notes |
|---|---|---|
| A. Colour overlay: transparent `InputTextMultiline` text, coloured text drawn on top via the draw list | Low–medium | Needs a monospace font and synced scrolling; fragile with selection, cursor, long lines, IME. Good spike, poor final solution. |
| B. Highlighted read-only view, click to edit in plain `InputTextMultiline` | Low | Recommended for Phase 3. |
| C. Real editor widget (C# port of ImGuiColorTextEdit, or own) | High | Only option that supports error underlines and autocomplete in place. |

## Interface stability audit

What lands on disk: `Usage: "Code"`, optional `CodeLanguage` in `.t3ui`, and annotation comments inside
users' code strings. Plausible next uses and whether they force a migration:

- More languages (GLSL, WGSL, Slang, Lua): new `CodeLanguage` string value. Safe.
- Annotating dropdown/enum or bool inputs (`// Mode: Blend {Add|Multiply}`): additive grammar extension.
  Safe, as long as `{` stays reserved now and is not used for ranges.
- Per-instance default values (`= 0.5`): **rejected**. Defaults belong to the symbol; per-instance defaults
  would make reset ambiguous.
- Hiding or grouping unannotated inputs: display only. Safe.
- Annotations for full shaders (`*FromSource`) that map cbuffer fields to inputs: same grammar. Safe.
- Exposing labels to the Player or exports: would need `Core`. Not planned; display only stays the rule.

## Open decisions

1. **Unannotated A–D when annotations exist.** Dim them, collapse them under a "More" group, or leave as is?
   Recommendation: dim. Collapsing changes the parameter layout and hides inputs that may still be connected.
2. **Graph node labels.** Should MagGraph input labels on the node also show the annotated name?
   Recommendation: yes, but after Phase 2 ships and the window behaviour has been tested.
3. **`Expression`.** One-line formula, no `//`. Options: skip it, allow a trailing `; A: Scale 0..1` section,
   or make `Formula` multiline. Recommendation: skip for now.
4. **Syntax.** `0..1` / `[0..1]` (recommended above) vs. the first-floated `{0..1}` / `{{0..1}}`.
5. **Multiple `Code` inputs on one symbol.** Recommendation: first one wins; none of today's ops has two.
