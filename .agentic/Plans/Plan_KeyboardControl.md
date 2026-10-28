# Keyboard control (graph navigation, parameter editing, key convention)

Inspired by text UIs like lazygit and Claude Code: a keyboard-first way to navigate graphs and edit
operators that stays discoverable. Mouse work (layouting, wiring at a distance) keeps the mouse; the goal is to
streamline the flows that dominate real sessions. This plan captures the design discussion; nothing is
implemented yet. Phase 0 and Phase 1 are detailed enough to build from.

**Frequency of use drives priority:** parameter change ≫ insert op ≫ pop out ≫ reorder (each step ≈ ×50 less
frequent). Reorder (Alt+↑/↓ in snapped stacks) already exists and is rarely useful because connection types
change along chains — leave it as is.

## Status

- **Phase 2, first step — implemented:** arrow keys move the selection in MagGraph when the graph has focus
  (`Editor/Gui/MagGraph/Interaction/SelectionNavigation.cs`, wired in `KeyboardActions`). Ranking by rect gap
  along the direction with a sideways penalty, connected items preferred; with nothing selected the item nearest
  the view centre is picked; the view pans to keep the selection visible. A trail makes the opposite key always
  walk back. While keyboard navigation is active (until the selection changes otherwise) subtle marks show each
  arrow's target, growing in staggered after each move. Manual test: `.tests-manual/graph-arrow-navigation.md`.
  Not yet: Shift+arrow extend, Space marks, zoom-to-edit-level.
- **Peek on P — implemented:** P pins right away (also in focus mode as graph background); releasing after
  0.25 s restores the previous output (`OutputPeek`, `ViewSelectionPinning.BeginPeek/CommitPeek/EndPeek`).
  P on the pinned op no longer unpins. The generic `TapOrHold` key flag is still open.
- **Action toasts — first version:** `Gui/UiHelpers/ActionToast` (bottom center of the main window, one at a time,
  `KeyHintText` markup, ~2.5 s, hover keeps it). Used by P: "Output shows X — hold P to only peek" until peek was
  used once (`UserSettings.Config.HasUsedOutputPeek`), and "X is already in the output — unpin it in the output
  window". Wording avoids "pinning" in messages. A tip icon (`Icon.Tip`, info blue) leads each message so it isn't
  mistaken for a log error. State lives in `UserSettings.Config.Toasts` (`ToastSettings`: Enabled, LearnedTips,
  TipShowCounts, MutedActions) keyed by string tip ids; `ActionToast.ShowTip(id, markup, maxTimes = 5)`,
  `MarkLearned(id)`, `ResetTips()`. Settings: "Show action notifications" and "Show Tips Again".
  Not yet: per-action mute UI (⚙) and the (?) help link.
- **Operator bookmarks — implemented:** Shift+digit toggles a bookmark on the selected operator (one digit per
  operator), the digit selects it and frames it via `ProjectView.TrySetCompositionOp(parentPath, …, childId)`,
  opening its composition. Stored per project (key: home symbol) in `UserSettings.Config.OperatorBookmarks` as
  path strings; badges with the digit at the operator's top left (`MagGraph/Interaction/OperatorBookmarks.cs`).
  The pop-out slot 0 idea is open; 0 is a normal slot for now. Unchecked: digit keys on AZERTY via SDL.
- **Already exists from other work:** Tab inserts below / Shift+Tab to the right of the selected op; delete keeps
  connections and closes gaps (Ctrl+Del deletes and disconnects).
- **Phase 1 prototype, second iteration — in progress:** "complete this step, continue with the most common
  next one". Grey suggestion text after the cursor says what Space does next.
  - Tab browser (`PlaceHolderUi`): `lp` → Space switches the list to LinePoints presets ("Default" first) →
    `tw` filters, ↑↓ picks → Enter inserts with the preset (one undo step with the insertion), a further
    Space inserts and opens the parameter prompt.
  - Parameter prompt (`ParameterPopover`, also on S): spreadsheet-style cursor. ↑↓ move; typing a value
    previews it; ↓/Tab apply and move on, Enter applies and closes, Esc reverts. Letters filter parameters;
    numbers (`10 +1 *2`), vectors (`0 1 0`, ` x 1`), Space toggles bools, Space on a dropdown (enum or
    int mapped to an enum) filters its options with ↑↓, ` r` resets. Colors (all Vector4 inputs): names with
    completion incl. `transparent`/`invisibleWhite`/`invisibleBlack` (`ColorValueParsing`), hex `#f80`,
    channels `a 0.5`, `orange a 0.5`, `*0.5` scales RGB only.
  - Hints: keys as keycap chips, text to type bright, descriptions faint (`Gui/UiHelpers/KeyHintText`).
  - Code: `MagGraph/Interaction/KeyboardEditing/` — `ParameterPopover` (partial: state and keys, `.Drawing`,
    `.Nudging`, `.Values`), `ParameterValueParsing` (pure parsing), `ValueExpression`, `ColorValueParsing`.
  - ←/→ nudge (number params, empty input): `InfinitySliderOverlay.DrawForKeyboard` shows the slider above the
    prompt bar. Press = one rounding step, hold = exponential acceleration (0.05 → 1.5 ranges/s over 2 s after
    0.25 s), stops on release. Shift ×0.1, Ctrl ×10 (mouse path still Alt ×10). Range/scale/clamping from the
    input's `FloatInputUi`/`IntInputUi`. ↓/Tab/Enter apply, Esc reverts the adjustment only.
  - ←/→ on other kinds: dropdowns step options, bools toggle; vectors `␣x` + ←/→ adjust a component (slider
    range from the vector's own UI settings); colors ←/→ adjust brightness (HSV value, HDR > 1 allowed),
    `␣h` hue (wraps), `␣s` saturation (0..1), `␣r g b a` channels. The arrow press turns the typed letter into a
    selector, so `␣g` + Enter is still gray and `␣r` + Enter still reset. HSV is captured at start so hue
    survives grays.
  - Verbs: `␣i` inserts an operator connected to the input (like clicking the input), `␣r` resets; immediate on
    numbers/vectors/bools/untypeable inputs, on Enter for colors and dropdowns.
  - **Popover (replaces the bottom bar and the Parameter Window highlighting):** bottom-centered in the graph,
    lists all parameters (max 9 rows, thin scrollbar), shrinks to the filter matches and scrolls only when the
    focus would leave the visible rows (a fixed focus row was tried and felt worse).
    Typing goes into a hidden ImGui input (callback keeps the caret at the end); rows draw name (filter matches
    underlined), value code-style right after it (animated orange, connected blue, modified brighter, defaults
    faint, color swatches), and in the focused row the typed value with caret and a faint result/suggestion.
    Help area: key chips for the current step plus the parameter's description. Tab (`⇥`) edits the value, Space
    still works. An operator typed first on a number inserts the current value (`*` → `100*`), expressions with
    `+ - * / % ^ ( ) pi` show `= 200`, `=` forces an absolute value (`=-5`). Esc steps back (value → filter →
    close). Applied values flash. Initial focus: the parameter last edited on that operator (session memory),
    else the first unconnected one. Closing by keyboard re-focuses the graph window.
  - Next: vector names (`up`, `+x`); decide whether the mouse slider moves ×10 from Alt to Ctrl; return to the
    prompt after `␣i` inserted an operator; dropdown options as popover rows.

### Backlog of ideas (from the popover review)

- Remember previously entered values per parameter; Alt+←/→ steps through them; show them as markers on the
  infinity slider.
- While the popover is open, draw the selected operator's outline much thicker (blinking is probably too much).
- Short transition from the operator's outline to the popover border when opening.
- Show the graph's key-hint row only once keyboard navigation is used.
- Graph shortcuts should work whenever no other window holds keyboard focus — e.g. after the symbol browser
  closes, re-focus the graph window the way the popover already does.
- Space vs Tab in the symbol browser (presets) — consider switching it to Tab as well.
  - Not yet: verbs `i c g x`, strings, preset names with spaces, multi-selection.

## Design principles

- **Nested modes with a visible stack.** Root (graph, nothing active) → Cursor (active op) → Param (`s`) →
  value entry. Overlays (palette, insert, presets) can open from any level. **Esc pops exactly one level** and
  is never undo; Ctrl+Z stays undo.
- **Contextual hint bar** at the bottom, lazygit-style: the current mode plus the keys valid right now. It is
  generated from the same scoped keymap that dispatches the keys, so it cannot drift from the real bindings.
- **Deterministic matching.** Filters rank by word prefix, then initials, ties by parameter/list order — never
  by usage frequency. The same keystrokes must do the same thing on the same op, or muscle memory breaks.
- **Keyboard and mouse share one UI.** The `s` prompt drives the Parameter window itself (highlight, dim,
  hint labels) instead of a separate popup; the nudge engine drives the existing infinity slider.
- **Explicit keyboard focus.** Bindings gated on `NeedsWindowHover` make the keyboard depend on the mouse
  position. Keyboard-driven modes need visible, unambiguous focus.

## Key convention

The factory keymap gets a written convention; every new binding takes its key from it.

| Modifier / group | Meaning |
|---|---|
| none | main action in context. Reserved: playback (Space, J K L), Esc, Tab, Enter, Del, and mode keys (`s`, `v`, `p`, `t`) |
| **Shift** | variant / store / reverse (extend selection, Shift+digit stores a bookmark, Shift+Tab inserts upstream, minor toggles like Shift+D disable). During continuous manipulation: **precision** (×0.1, snapping inverted while dragging) |
| **Ctrl** (Cmd on macOS) | app/document commands (save, copy, undo, duplicate); Ctrl+P command palette. During continuous manipulation: **coarse** (×10) |
| **Alt** | **navigation**: Alt+←/→ history (unchanged), Alt+↑/↓ parent composition / into the active op (I/U stay as aliases) |
| digits | 1–9 jump to op bookmark, Shift+1–9 store; 0 = slot for popped-out ops |
| F-keys | F1–F10 load layout, Ctrl+F1–F10 save; F11 fullscreen, F12 focus mode |
| tap vs hold | tap latches, hold is momentary while the key is down (`p` pin/peek, `t` scrub) |

Decisions:
- **Alt is not view/pan.** Keyboard panning is unnecessary with cursor-follow. Alt is also a poor *mouse*
  modifier on Linux (KDE/GNOME grab Alt+drag for window moves).
- **Infinity slider ×10 moves from Alt to Ctrl** (`InfinitySliderOverlay.GetKeyboardScaleFactor`), so the
  modifier meaning is identical for mouse scrubbing and keyboard nudging.
- **No positional (scancode) bindings** — too much engineering for an editor that isn't a game. Instead the
  **layout-safe key rule**: factory bindings only use keys that are unshifted and not behind AltGr on US,
  German QWERTZ and French AZERTY:
  - safe: letters (chosen by mnemonic), digits*, `,` `-`, Space, Tab, Enter, Esc, Del/Backspace, arrows,
    Home/End, PgUp/PgDn, F-keys
  - avoid: `/` `:` `;` `[` `]` `\` `'` `` ` `` `=` and `.` (Shift on AZERTY)
  - inside text prompts (`s`, Tab search, palette) any character is fine — that is typed text, not a binding.
  - \* AZERTY digits need Shift. Win32 still reports VK_1…VK_9; the SDL path translates keycodes
    (`SdlPlatform/SdlKeyMap.cs`, "letters and digits follow the keyboard layout") — verify with an AZERTY
    layout before digit bookmarks depend on it.
- **WASD camera on AZERTY:** ship a second factory keymap "Default (AZERTY)" with ZQSD instead of scancode
  plumbing. `KeyMapSwitching` already supports named maps (the current one is "Default (QWERTY)").
- **Command palette = Ctrl+P** (VS Code / Sublime), lists every `UserActions` entry with its current
  shortcut, so it doubles as a way to learn bindings. "Display as background" moves to Ctrl+Shift+P; "clear
  background" becomes palette-only.
- **Camera on arrow keys** (Shift pan, Alt zoom, Ctrl look around) is layout-independent but breaks the Shift
  = precision and Alt = navigation rules. Open question; if adopted, document "in camera context modifiers
  choose the motion axis".

### Current bindings that violate the convention

| Binding | Problem | Proposal |
|---|---|---|
| Shift+←/→ previous/next frame | collides with Shift+arrow extend selection | move (open: `,` + something for keyframe jumps) |
| `.` next keyframe | needs Shift on AZERTY | move |
| Alt+D disconnect, Alt+A align left | Alt is navigation | Shift+key or palette-only |
| Ctrl+P / Ctrl+Shift+P background image | Ctrl+P becomes palette | Ctrl+Shift+P / palette |
| Z / X tap beat sync | Z moves on QWERTZ/AZERTY | acceptable as mnemonic-free pair? decide |
| Timeline drag modifiers (Alt = source link, Ctrl+Alt = slip in `TimeClipInteractions`) | Alt meaning | mouse-drag modifiers inside the timeline; review once the keyboard rules settle |

Consider shipping the cleaned map as the new "Default" and keeping today's map as "Legacy" so existing users
keep their muscle memory.

## Modes and interactions

### Cursor: active op and selection (Blender model)

- Selection is a set plus one **active** op (the cursor). Clicking sets the active op too.
- **Arrows** move the active op and collapse the selection to it. They follow snapped MagGraph topology first
  (↑/↓ along vertical stacks, ←/→ along horizontal inputs) and fall back to the nearest op in that direction.
- **Shift+arrow** extends the selection. **Space** marks/unmarks the active op without moving (lazygit).
  Space keeps play/pause when the timeline or output has focus; J/K/L remain the playback keys everywhere.
- Per-op actions (params, presets, peek, insert) target the active op; batch actions (bypass, disable,
  dissolve, layout) target the selection.
- **Viewport follow:** pan when the active op enters a ~15 % edge margin; snap to the default edit zoom only
  if zoomed out below label readability. After a manual mouse pan, stop following until the next cursor move.

### `s` — parameter prompt (highest priority)

`s` opens the active op's parameters in the Parameter window, first one semi-focused.

Grammar: **letters filter → lock → verb or value.**
- letters filter the list (word prefix, then initials: `cm` = ColorMode); shortest unique prefixes are shown
  as hint labels next to the parameter names
- **↑/↓** move the parameter focus at any time, also without filtering
- **Space or Enter locks** the focused parameter; after locking, letters are verbs
- **a digit, `+`, `-`, `*` or `.` locks implicitly** (parameter names never start with them):
  `s c 100⏎` sets Count; `s le +.01` nudges Length by 0.01 and makes 0.01 the ←/→ step until the prompt closes
- no auto-lock on a unique match — uniqueness depends on the op and would make keystrokes op-dependent

| Keys | Action |
|---|---|
| `s` ↑↓ | focus parameter |
| `s` ←→ | nudge focused parameter (see nudge engine) |
| `s c 100⏎` | set value; relative input `*2`, `+10` |
| `s c ␣ r` | reset to default |
| `s c ␣ i` | insert a placeholder op connected to the parameter, symbol browser focused. Esc removes it and returns to `s c` (locked); a second Esc closes the prompt. After picking, the cursor stays on the original op; Shift+Enter picks and jumps to the new op |
| `s c ␣ c` | connect mode: label nearby ops with a **type-compatible output** 1–9, ranked by screen distance; letters filter candidates by name. `3` connects, **Shift+3 connects and makes it the active op** |
| `s c ␣ g` | jump the cursor to the op feeding a connected parameter (also general navigation) |
| `s c ␣ x` | disconnect |

Types: vectors use Tab or `.x/.y/.z` for a component sub-cursor; enums cycle with ←/→ and fuzzy-match typed
names; bools toggle with Space; colors use hex or an H/S/V sub-cursor. Connected parameters can't be nudged:
the hint bar offers `g` and `x`. Typed values preview live; Esc reverts; commit = one undo entry.

With several selected ops of the same type, `s` sets the value on all of them.

### Nudge engine = keyboard-driven infinity slider

`InfinitySliderOverlay` already has log10 tick decades, a damped modifier scale and an original value for Esc.
Keyboard nudging drives the same overlay, anchored at the parameter row:
- ←/→ move the value; holding accelerates (damped like WASD camera movement), stops dead on release
- modifiers choose the decade (Shift ×0.1, Ctrl ×10); ↑/↓ stay parameter switching
- base step from the input's UI scale (`FloatVectorInputValueUi.Scale`; ints step by 1)
- one undo entry per held gesture
- `t` time scrubbing (`PlaybackTimeScrubHandling`, hold-only today) gets ←/→ for free

### Tab — insert, with presets

- Tab inserts a snapped op **downstream** of the active op; Shift+Tab **upstream**. The new op becomes active,
  so `Tab grid ⏎ Tab blur ⏎` builds a chain.
- `Tab lp ␣ vert ⏎` inserts LinePoints with the "Vertical" preset. The grammar already exists:
  `SymbolFilter.UpdateFilters` splits the search on whitespace into symbol and preset filter, and the legacy
  `SymbolBrowser` lists matching presets. MagGraph's `PlaceHolderUi` has that block commented out — restore it.

### `v` — presets panel

Presets need more than "pick one", so they get a dedicated key (`v`, free, matches "Variations"). Build on the
reusable picker from `Plan_VariationPicker.md`.

| Key | Action |
|---|---|
| type + ⏎ | apply matching preset |
| ←/→ on a preset | blend from current values toward it (nudge engine, hold to accelerate) |
| `n` / `o` | save current values as new / overwrite focused |
| `r` / `x` | rename / delete |
| `1` `2` `3` | change the focused preset's scope |

**Preset ownership** (real gap). Today `SymbolVariationPool.ResolveVariationFilePaths` writes to the
symbol's package `.meta/Variations/{symbolId}.var` when the package is writable; only read-only packages
(Lib in standalone builds) use the shipped file as defaults plus a per-user overlay in AppData. In a dev
checkout user presets therefore mix into the shipped built-ins, and per-instance presets don't exist.

| Scope | Lives in | Example |
|---|---|---|
| Built-in | the symbol's package, read-only for users | LinePoints "Vertical" |
| Mine | user folder, available in every project | a favourite Blur look |
| This op | the project, keyed by composition symbol + child id | "intro" / "drop" states of one Blur |

"This op" overlaps with snapshots (values per child of a composition) — check whether it can be snapshot data
filtered to one child before designing new storage.

### `p` — pin (tap) and peek (hold)

- Tap keeps today's latched pin to the output window.
- Hold shows the active op in the output while the key is down; on release the pinned op returns. Peek never
  loses the primary pinned output.
- Generic **`TapOrHold`** flag in `KeyActionHandling` next to `KeyHoldOnly`: release within ~250 ms = tap.
  Candidates beyond peek: snapping, idle motion (M). Final Cut's N (snapping) is the model.

### Bookmarks and pop / put

- Op bookmarks show a 1–9 badge in the graph. `1` selects that op and brings it into view; Shift+1 stores
  the active op. Stored per project; badges only show for ops in the current composition; jumping to a
  bookmark elsewhere opens that composition (`IGraphView.OpenAndFocusInstance` exists).
- Merge with today's view bookmarks (`GraphBookmarkNavigation`, Ctrl+digit = pan/zoom only): a bookmark
  stores op + view, Ctrl+digit becomes free.
- **Pop out:** unlink the active op, close the gap (same logic as dissolve), move it to free space, cursor
  moves up/left. The popped op goes into **slot 0**. `0` brings it into view; **put** inserts it at the cursor.

### Action feedback toasts

When a hotkey is triggered by accident, the user should see what happened. Figma-style toasts show the
outcome plus the shortcut that caused it: `Pinned [Blur] to output  [P]`.

**When to show one.** Opt-in per action via a new `Flags.ShowFeedback` in `KeyActionHandling` (next to
`KeyHoldOnly`). Rule: show a toast when the effect is **invisible, off-screen, or overwrites something**.

| Show | Example message |
|---|---|
| pin to output | `Pinned [Blur] to output` [P] |
| toggles without visible state | `Idle motion off` [M], `Snapping on` |
| overwrites | `Saved window layout 3` [Ctrl][F3], `Stored bookmark 2` [⇧][2] |
| clipboard | `Copied 4 operators` [Ctrl][C], `Pasted values to [Blur]` |
| time range | `Start time set to 12.0` [B] |

Not for navigation, nudging, playback, or anything with an obvious visible result in the graph (bypass,
disable, delete).

Behaviour:
- the message states the **outcome**, not the action name; it includes the op name, and toggles say the
  new state ("off", not "toggled")
- only for keyboard triggers — from a menu or the palette the user already knows what they did
- repeats replace instead of stacking (`Copied 4 operators` → `Copied 5 operators`)
- ~2 s, then fade; hovering keeps it visible
- undoable actions get a small Undo button
- bottom centre, just above the hint bar. Not in `StatusErrorLine`, which is the (noisy) log channel; the
  message can additionally be logged at Debug level for history
- zero per-frame cost when no toast is visible; the text is formatted once at trigger time

**Toast icons** (`Icon.Help`, `Icon.Settings` from the atlas), shown at the right edge of every toast:
- **(?) help** opens the help window on the action's topic via `HelpWindow.ShowTopic(new HelpTopic(Guid.Empty,
  key))`. Topic keys follow a fixed naming, `ui:action.<UserActionName>` (e.g. `ui:action.PinToOutputWindow`),
  so no extra registration is needed and the docs can be written with the existing `write-topic-docs` flow.
  Hide the icon when the topic has no doc yet.
- **(⚙) settings** opens a small popover for this action:
  - "Don't show this notification again" (per-action mute, stored in `UserSettings`)
  - "Change shortcut…" — opens the keymap editor focused on this action
  - the action's own behaviour options where they exist (e.g. delete: dissolve vs plain delete; pin:
    tap/hold threshold)
  - "Turn off all action notifications" (global switch)
- Mouse only: a toast is about an accident that just happened, so the user isn't in a keyboard flow. Hover
  pauses the fade so the icons stay reachable.

### Shortcut labels

Always **uppercase keycaps with modifiers spelled out**, never lazygit-style `c` vs `C` (hard to read, hides
that Shift is involved). Drawn as small keycap chips:

| Platform | Example |
|---|---|
| Windows / Linux | `[⇧][P]`, `[Ctrl][⇧][S]`, `[Alt][←]` |
| macOS | `[⇧][P]`, `[⌘][⇧][S]`, `[⌥][←]` |

- `⇧` is printed on the Shift key of most layouts (including German "Umschalt"); Ctrl/Alt stay words on
  Windows/Linux because `⌃`/`⌥` mean nothing to most non-Mac users. Extends `KeyCombination.CtrlLabel`.
- fixed modifier order: Ctrl, Alt, Shift, key
- grammar sequences show as separate chips: `[S] [C] [␣] [R]`
- one renderer, `KeyCombinationUi.Draw()`, shared by hint bar, toasts, tooltips and the palette. ImGui menu
  shortcut columns are plain text and keep the text form from `KeyCombination.ToString()` (`Shift+P`).
- fonts request U+2190–21FF (`UiContentUpdate.GetExtendedGlyphRanges`), so `⇧` should render if the font
  file contains it — verify. `⌘` (U+2318) and `⌥` (U+2325) are outside the range and need adding for macOS.

### Delete = dissolve

Default delete keeps connections: close the gap when the neighbours' types match, otherwise plain delete.
Afterwards the cursor moves to the upstream neighbour so repeated deletes keep working.

## Current state — what exists

- `Editor/Gui/Interaction/UserAction.cs` — flat `UserActions` enum; `SelectToAbove/Right/Below/Left` exist.
- `Editor/Gui/Interaction/Keyboard/FactoryKeyMap.cs` — one flat map. Overlaps are resolved only by window
  focus/hover (`C` = InsertKeyframe and CameraFocusSelection, `F` = FocusSelection and CameraReset).
- `KeyBinding.IsContextValid` / `KeyActionHandling.Flags` — `NeedsWindowFocus`, `NeedsWindowHover`,
  `RemainActiveWhenItemActive`, `KeyHoldOnly`, `KeyPressOnly`. No notion of modes.
- `Editor/Gui/MagGraph/Interaction/KeyboardActions.cs` — cursor navigation is a commented-out TODO.
  `GraphStates` / `context.StateMachine` already gate some actions on `GraphStates.Default`.
- `InfinitySliderOverlay`, `PlaybackTimeScrubHandling` (T hold, Esc restores) — reusable for nudging.
- `SymbolFilter` two-part search, `SymbolVariationPool`, `VariationPicker` plan — presets.
- `GraphBookmarkNavigation.Bookmark` — view-only bookmarks.
- No toast/notification widget. `StatusErrorLine` shows the latest log line, fading, in the menu bar.
- `KeyCombination.ToString()` produces text labels (`Ctrl+Shift+P`); `KeyMap.UpdateShortcutLabels` caches them.

## Phases (each shippable)

**Phase 0 — scoped keymap + hint bar + convention.**
- Add a scope/mode dimension to `KeyBinding` (Root, Cursor, Param, ValueEntry, Palette, Timeline, Output…);
  a mode stack owned by the editor; the dispatcher resolves the innermost mode first, then falls through.
- Hint bar listing the bindings of the active mode (allocation-free per frame: cache the label list per
  mode, rebuild on mode or keymap change).
- Visible keyboard focus. Audit `NeedsWindowHover` bindings.
- `KeyCombinationUi` keycap chips (shared by hint bar and toasts); action feedback toasts with
  `Flags.ShowFeedback` on the first set of actions (pin, layout/bookmark save, copy/paste, invisible toggles).
  Toast (?) and (⚙) icons: help topic keys `ui:action.<UserActionName>`, per-action mute, "change shortcut".
- Apply the convention to the factory map (Alt-free nudging, Ctrl+P palette, moved bindings above); decide
  Default vs Legacy map. Write the convention into `.help/` as user documentation.

**Phase 1 — `s` parameter prompt + nudge engine.**
- Prompt state machine (filter → locked → verb/value) driving the Parameter window; hint labels.
- Nudge engine on `InfinitySliderOverlay` (keyboard input path, Ctrl ×10); per-gesture undo.
- Verbs `r`, `i`, `c`, `g`, `x`; typed values with relative input; multi-selection set.

**Phase 2 — cursor navigation.** Active op, topology-first arrows, Space marks, viewport follow.
(Can run in parallel with Phase 1 — `s` works on the selected op without a cursor.)

**Phase 3 — Tab insert with presets.** Downstream/upstream insert, restore preset list in `PlaceHolderUi`.

**Phase 4 — presets panel + ownership scopes.** `v` panel on `VariationPicker`, blend by arrows, three scopes.

**Phase 5 — peek + TapOrHold flag.**

**Phase 6 — bookmarks badges, pop/put, dissolve-delete.**

**Phase 7 — command palette (Ctrl+P), AZERTY factory map.**

Each phase adds a manual test set in `.tests-manual/` and updates `.help/`.

## Open questions

1. Esc at Root: nothing, or go up to the parent composition?
2. Camera on arrow keys with modifiers as motion axis, or WASD (+ AZERTY map) only?
3. Default vs Legacy factory map split?
4. Typing a value on an animated parameter: keyframe at current time, or edit the base value?
5. `s` with mixed-type selection: intersection of shared parameters, or only same-type selections?
6. Should "Mine" presets be syncable/exportable across machines?
7. New home for frame stepping and keyframe jumps (Shift+←/→ and `.` are freed).
8. Do digit bindings work on the SDL path with an AZERTY layout?
9. Does the UI font contain `⇧`, or does it need an atlas icon?
10. Besides the toast's own ⚙ popover, should there be a live-performance mode that suppresses toasts entirely?
