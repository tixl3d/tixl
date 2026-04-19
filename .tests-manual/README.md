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
prerequisites:                    # optional — free-text setup requirements
  - An empty project is open
related-help:                     # optional — relative links into .help/
  - ../.help/using/graph-window.md
---

Short prose intro (optional): what this set covers, who should run it,
and anything that applies to all steps.

## Step: Open the symbol browser

**Context:** On the Graph Window.
**Action:**
- Press `Tab`

**Expected:**
- The Symbol Browser opens.
- The search field is focused.

## Step: Search and create a RadialGradient

**Context:** Symbol Browser is open.
**Action:**
- Type `RG`
- Press `Enter` to accept the top result

**Expected:**
- A `[RadialGradient]` operator is created.
- It is selected.
- Its inputs show in the Parameter Window.
```

### Field conventions

- **Step delimiter** — each step begins with `## Step: <short imperative title>`. No nested headings inside a step; use bullet lists.
- **`**Context:**`** — one sentence establishing where the tester is. Can be omitted if continuous from the previous step.
- **`**Action:**`** — bullets, imperative mood. One keystroke / click per bullet where practical.
- **`**Expected:**`** — bullets, present tense, observable outcomes only. No "should probably" language; if the outcome is fuzzy, split the step.
- **Links** — use relative paths into `.help/` for deep dives. Keep prose scannable — the step card in the runner is small.

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

## Authoring tips

- Write for someone who has never used TiXL — name menus, buttons, and windows explicitly.
- One observable change per step. If the tester needs to check two unrelated things, split.
- Avoid absolute coordinates ("click at 200,400"). Use names (`Graph Window`, `Parameter Window`, `[RadialGradient]`).
- Prefer keyboard triggers over mouse drags where both work — easier to describe unambiguously.
- If a step depends on earlier state, say so in its `**Context:**` — steps may not always be run top-to-bottom once the runner allows partial runs.
