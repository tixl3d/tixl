# Multi-selection parameter editing

Split out of `Plan_KeyboardControl.md` (2026-11-03). Nothing is implemented.

## Goal

Change a parameter on several selected operators in one step, from the keyboard popover and from the Parameter
window, as one undo entry.

## Current state

- `ParameterPopover.Open` only opens for exactly one selected operator (`Selection.Count != 1` returns early,
  `ParameterPopover.cs`), and closes when the selection changes.
- The Parameter window edits one instance; it creates a single `ChangeInputValueCommand` per gesture
  (`ParameterWindow.cs`, `_inputValueCommandInFlight`). It has no multi-selection handling.
- `MacroCommand` exists and can bundle one `ChangeInputValueCommand` per operator into a single undo step.

## Proposal

- **Same symbol** (all selected ops are the same type): list that symbol's parameters. Typed values, relative
  input (`*2`, `+10`), nudging, reset and toggles apply to every op. Relative input applies per op, so `*2`
  doubles each op's own value.
- Values that differ between the ops show as mixed (for example `–` or a faint range) instead of the first op's value.
- Connected or animated inputs on some of the ops are skipped and reported in the help area, not overwritten.
- One `MacroCommand` per committed edit or held nudge gesture.
- Verbs that act on one connection (`␣i`, `␣g`, `␣x`) are disabled with several ops selected.

## Open questions

1. Mixed types: offer the parameters they share (same name and type), or require the same symbol?
2. Nudging with differing values: move all by the same delta, or scale each proportionally?
3. Does the Parameter window get the same behaviour in this step, or only the popover first?
4. Which op is the "primary" for initial focus and the slider range: the first selected or the last clicked?

## Done when

- Popover and (if in scope) Parameter window edit all selected ops; Ctrl+Z undoes the whole edit at once.
- A manual test set in `.tests-manual/` and the popover section in `.help/` cover it.
