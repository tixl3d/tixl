# Manual Test Sets

Human-run verification walkthroughs for TiXL features. Each file defines a **test set** — an ordered list of **steps** exercising one feature or flow. Tests are written in plain markdown so contributors can follow them without tooling; the frontmatter is structured so a future in-editor runner can parse and drive them (see [`Plan_ManualTestRunner.md`](../.agentic/Plans/Plan_ManualTestRunner.md)).

## Layout

```
.tests-manual/
├── README.md                   (this file)
├── creating-operators.md       (one file per test set)
├── dopesheet-curve-expand.md
└── ...
```

One test set per file. Filename = the `id` in frontmatter, kebab-case. Test sets are flat — no subdirectories yet. If the directory grows past ~30 sets, revisit grouping.

## File format

```yaml
---
id: creating-operators            # kebab-case, unique, matches filename
title: Creating Operators         # human-readable, shown in runner UI
scope: graph-window               # broad feature area (free-form tag)
tags: [smoke, essential]          # optional — used by the runner to filter sets
added: 2026-05-31                 # ISO date the set was first added — drives "Recently added" sort
added-in-version: 4.2             # TiXL major.minor the set first shipped in
prerequisites:                    # optional — free-text setup requirements
  - An empty project is open.
related-help:                     # optional — relative links into .help/
  - ../.help/using/graph-window.md
---

Short prose intro (optional): what this set covers, who should run it,
and anything that applies to all steps.

## Step: Opening the Symbol Browser

**Action:**
With the Graph Window focused, press `Tab`.

**Expected:**
- The Symbol Browser opens.
- Its search field is focused and empty.

## Step: Finding an operator by name

**Action:**
With the Symbol Browser open, type `RG`.

**Expected:**
- The result list filters down to operators whose names contain those letters.
- `[RadialGradient]` is visible somewhere in the list.
```

### Field conventions

- **`added`** *(required for new sets)* — the ISO date (`YYYY-MM-DD`) the set was
  first added. Drives the runner's "Recently added" sort. Legacy sets without it
  sort as oldest. **`added-in-version`** — the TiXL `major.minor` it first shipped
  in (e.g. `4.2`). Both were backfilled from git history for existing sets.
- **Step delimiter** — each step begins with `## Step: <short imperative title>`.
  The title is shown in the runner subtitle ("Step 3/12 — Creating an operator"),
  so phrase it as the thing being verified, not the keystroke.
- **`**Action:**`** — the instruction to the tester. Write it as prose, the way
  you'd guide a new user: "With the search results visible, use the cursor
  up/down keys to highlight `[RadialGradient]`." Use bullets only when steps
  are genuinely parallel (e.g. "either click this, or press Enter"). Avoid
  one-keystroke-per-bullet — it reads like a checklist instead of a tour.
- **`**Expected:**`** — present tense, observable outcomes only. Bullets are
  fine here since they're separate checks. No "should probably" language; if
  the outcome is fuzzy, split the step.
- **`**Context:**`** *(optional, legacy)* — one sentence establishing where
  the tester is. Older sets used this; new sets should fold the context into
  the first sentence of `**Action:**` instead. The runner still parses it for
  back-compat and merges it into the Action body at display time.
- **Links** — use relative paths into `.help/` for deep dives. Keep prose
  scannable — the step card in the runner is small.

### Step outcomes

Runtime outcomes a tester can record per step: `pass` / `fail` / `other` (with free-text comment). These are not stored in the file — they belong to a run, not to the test definition.

## Process — when to add or update a set

Mirror of the `.help/` rule in the project `CLAUDE.md`: any PR that changes user-visible UI or behavior must extend an existing test set or add a new one, in the same PR. Feature plans under `.agentic/Plans/` link to their test set rather than duplicate the steps.

Stale tests are removed with the feature they covered.

## Tagging guidelines

Tags are free-form but standardize on a short core vocabulary so the runner can offer sensible filters:

- `smoke` — under 60 s, run on every build
- `essential` — primary happy path for a feature
- `edge` — edge cases, regression nets
- `perf` — performance-sensitive observation steps
- `flaky` — known intermittent; keep until fixed

### Audience — every set carries exactly one

Each set is tagged for **who** runs it, so the runner can present two clean lists:

- `user` — an artist verifying something they'd actually do in TiXL: load a project, set an
  audio source, record, export. Written in plain language — name things by what the user sees on
  screen, not by the file, format, or class behind them.
- `dev` — a contributor verifying editor internals or the authoring/build workflow: creating
  operators, undo/redo of graph edits, build-failure messages, the markdown renderer. These keep
  their technical detail — their audience wants it.

When a set could read either way, pick by who is *expected to run it*: if a non-coding artist can
follow it to completion, it's `user`.

## Authoring tips

- Write for someone who has never used TiXL — name menus, buttons, and windows explicitly.
- One observable change per step. If the tester needs to check two unrelated things, split.
- Avoid absolute coordinates ("click at 200,400"). Use names (`Graph Window`, `Parameter Window`, `[RadialGradient]`).
- Prefer keyboard triggers over mouse drags where both work — easier to describe unambiguously.
- If a step depends on earlier state, say so in its `**Context:**` — steps may not always be run top-to-bottom once the runner allows partial runs.
