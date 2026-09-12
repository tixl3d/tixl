# Feature Review Guide

Agent-neutral review procedure for a TiXL feature or change set. Any agent (Claude, Codex, a
human) runs it the same way; the Claude skill `review-tixl-feature` is only a pointer to this file.

A code review tuned to TiXL: a realtime, ImGui-driven editor where most code runs every frame and
lives for years. The bar is not "does it work" but "would a maintainer be glad this is here".

Read `.agentic/AGENT_INSTRUCTIONS.md` first if it isn't already in context; it is the style and
performance contract this review enforces.

## Scope

The argument names what to review: a feature name, a file list, a commit range, or nothing. With
nothing, review the dirty working tree (`git status --short`, then `git diff` plus new files).
Follow references outward only as far as needed to judge the change: read the callers of new public
members and the types a new class depends on.

Never commit, never push, never use `gh`. Never apply a fix unless the user asked for fixes in this
turn; the deliverable is the findings list.

## The four lenses

Read every changed file completely, then look through each lens in turn. Note candidates as you
go; judge them afterwards so a weak finding can be dropped.

### 1. Elegance

Solving a problem by adding more code is easy. Well-crafted code is small, reads top to bottom,
and bends without breaking. Look for:

- **Accretion.** Iteration leaves layers behind: a guard for a case the new design can't produce,
  a field that is written but no longer read, a comment describing the previous algorithm, a
  parameter every caller passes the same value for. Each one is a finding.
- **Wrong altitude.** A class that mixes plumbing (threads, buffers, IO) with policy (the
  algorithm) is hard to test and hard to read. Prefer a pure data or algorithm class plus a thin
  host. `RollingMetric` + `MetricGraphView` is the reference split.
- **Duplicated knowledge.** The same constant, formula, or unit conversion in two places. The
  same "bars = seconds * bpm / 240" spelled out three times is a helper waiting to exist.
- **Mode flags.** An enum or bool that makes one method do two things via branches, when two
  small implementations behind one interface would read better.
- **Comments that narrate history.** "Was X before", plan or ticket references, session context.
  Only the lasting *why* survives.

Do not propose rewrites for style alone. A finding here must name what becomes simpler.

### 2. Naming

Every class, method, field, and constant: does the name say what it is, in the vocabulary the
rest of the codebase uses?

- Check the project's vocabulary rules: enums are plural (`SyncModes`), static services end in
  `Handling`, outliner entries are "items", graph links are "connections", not edges.
- **The description test.** Write the one sentence a reader would need about the member, then check
  whether the name says the same thing. "How many bars the tempo is averaged over" is not
  `IntervalCapacity`; it is `MaxBarsAveragedForTempo`. If the sentence and the name disagree, the
  name is wrong, not the sentence. But the name is not the sentence: as short as possible, as long
  as necessary. `MinBarDurationSec` is complete; `ShortestAcceptedBarDurationSec` is padding.
- A comment that repeats an identifier, or restates what the name already says, is a finding: the
  fix is a better name, not a longer comment. A note that does earn its place on a member goes in a
  doc comment, not a `//` line, so it shows on hover: `/** one line */` on private members,
  `/// <summary>` on public and internal API.
- A name that needed a comment to explain it is a finding.
- A name that could be read two ways ("selection of X" versus "selection within X") is a finding.
- A boolean that isn't a predicate (`Locked` instead of `IsLocked`, `Estimate` instead of
  `HasEstimate`) is a finding.
- Public members: is the visibility earned? `internal` or `private` when no outside caller exists.

### 3. Robustness

Walk each new code path with a hostile mind:

- **Null and lifetime.** Nullable fields read without a check; references to `Instance`, `Symbol`
  or `SymbolUi` held across frames or reloads instead of Guids; disposed resources used after
  `Reset`/`Stop`.
- **Initialization order.** Static fields that depend on other statics; lazy init raced from two
  threads; a value used before the code that sets it has run (first frame, first callback).
- **Threading.** Anything touched from an audio callback, worker thread, or IO completion: is
  every shared field either under one lock, `volatile`, or an atomic? Are locks taken in a
  consistent order? Is a long operation (model load, file IO) ever done under a lock or on the
  callback thread?
- **Arithmetic.** Division by a value that can be zero; `%` on negative numbers; `(int)` casts of
  large doubles; NaN propagation from an uninitialized sentinel.
- **Exceptions.** Native calls, file IO, and parsing wrapped so a failure degrades (log once,
  disable feature) rather than tearing down the frame loop. A `catch` that swallows silently is a
  finding too.
- **Serialization.** New persisted fields: written and read in *every* reader, with a sane
  default for files that predate them.

### 4. Realtime performance

Identify which methods run per frame, per audio callback, or per event, and check those only;
don't micro-optimise cold paths.

- **Allocations.** `new`, LINQ, string interpolation, `foreach` over non-struct enumerators,
  closures, boxing (`object` parameters), `params` arrays, `ToString` on values. The tolerance is
  zero in code that runs every frame while the editor is in normal use: graph canvas, timeline,
  output windows, operator updates, audio callbacks. Dialogs and settings windows drawn only while
  open are not hot: a formatted status string there is fine if it keeps the code simple, and is
  not a finding.
- **Work.** Loops whose bound grows with data, lookups by string, `Array.Sort` per frame, locks
  held around more than a few field copies.
- **ImGui rules.** Pixel literals not multiplied by `T3Ui.UiScaleFactor`, `new Color(...)` in draw
  code, native sliders instead of `FormInputs`, fonts scaled and not reset.
- **Off-thread work.** Is heavy work (inference, decoding) off the frame and audio threads, and is
  the hand-off allocation-free in steady state?

## Output

Rank findings most severe first; a crash or data-loss risk outranks a naming nit. For each:

```
N. <file>:<line> — <one-line claim>
   Problem: <what goes wrong or what is hard to read, concretely>
   Fix: <the specific change, small enough to act on>
```

Group under the four lens headings; leave a heading out if it has no findings rather than padding
it. End with a two-line verdict: is the feature ready as it stands, and which findings are
blocking. Keep the whole report short enough to read in one sitting; ten strong findings beat
thirty weak ones.

If the user asked for fixes, apply them after the report in severity order, build the affected
project (`dotnet build`, the configuration the running editor is *not* using), and state per
finding whether it was fixed, skipped, or needed no change.
