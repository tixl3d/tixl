# In-Editor Manual Test Runner

**Date:** 2026-04-19
**Status:** Phase 1 landed (2026-05-01) — parser + Pick + Run + in-memory Summary. Export and polish phases pending.

## Goal

Let testers (internal and community) run manual test sets from inside the TiXL editor: pick sets, walk through steps, record pass / fail / other + comment per step, and export results for sharing.

Source of truth for test content is [`.tests-manual/`](../../.tests-manual/README.md). The runner is a thin UI over those markdown files.

## Non-goals (v1)

- No automated UI-driving (the runner doesn't press keys or click buttons for the user).
- No authenticated GitHub API posting — export path is copy-to-clipboard + pre-filled issue URL.
- No persisted run history inside the editor beyond the current session.
- No partial-reorder of steps — runs are linear through the selected sets.

## UX — three states

### 1. Pick

- ImGui window titled `Manual Test Runner`. Invoked from the Help or Dev menu (decide during impl; behind a user settings flag initially if we want to keep it out of the way).
- Lists every test set parsed from `.tests-manual/*.md` with a checkbox, title, scope, tag chips, and step count.
- Filter row at the top: text search + tag multi-select (`smoke`, `essential`, `edge`, ...).
- Buttons: **Start Run** (disabled if nothing selected), **Cancel**.

### 2. Run

- Single large card showing the current step, mirroring the Figma sketch:
  - Header: set title + step index (e.g. `Creating Operators — 2 / 3` and global `2 / 27` across selected sets).
  - Body: step title, Context, Action (bulleted), Expected (bulleted).
  - Side: `Actual Result` free-text comment box.
  - Footer buttons: **Success** (green), **Fail** (red/pink), **Other...** (neutral — requires non-empty comment).
  - Navigation: `←` `→` arrow buttons and `Esc` to abandon the run. Entering an outcome auto-advances.
- Optional: deep-link the mentioned help page (if any in `related-help`) as a side link the tester can open.

### 3. Summary

- Per-set breakdown: `N pass / N fail / N other / N skipped`.
- Full step-by-step list with outcomes and comments.
- Export buttons:
  - **Copy JSON** — full run payload to clipboard.
  - **Copy Markdown** — a human-readable summary (good for pasting into Discord / Slack).
  - **Open GitHub Issue** — launches the default browser to `https://github.com/<repo>/issues/new?title=...&body=<urlencoded markdown>`. Body is pre-filled, user reviews and submits. No auth required.
- **New Run** resets to Pick.

## Data model

```csharp
// in-memory only, not serialized to the project
sealed record TestSet(string Id, string Title, string Scope,
                     IReadOnlyList<string> Tags,
                     IReadOnlyList<string> Prerequisites,
                     IReadOnlyList<TestStep> Steps,
                     IReadOnlyList<string> RelatedHelp);

sealed record TestStep(string Title, string? Context,
                       IReadOnlyList<string> ActionBullets,
                       IReadOnlyList<string> ExpectedBullets);

enum Outcome { Pending, Pass, Fail, Other, Skipped }

sealed record StepResult(string SetId, int StepIndex,
                         Outcome Outcome, string? Comment,
                         DateTime TimestampUtc);

sealed record RunReport(DateTime StartedUtc, DateTime FinishedUtc,
                        string EditorVersion, string OsVersion,
                        IReadOnlyList<StepResult> Results);
```

## Parsing `.tests-manual/*.md`

- YAML frontmatter between leading `---` fences.
- Body split on `^## Step: ` headers. Each step body scanned for `**Context:**`, `**Action:**`, `**Expected:**` blocks. Action / Expected bullets are the `-` list items that follow the header.
- Lenient: missing Context is allowed; malformed step emits a parse warning in the Pick UI but doesn't crash the runner.
- Use an existing YAML dependency if one is in the solution; else a tiny hand-rolled parser (frontmatter is minimal).

## Export payloads

### JSON

```json
{
  "startedUtc": "2026-04-19T12:34:56Z",
  "finishedUtc": "2026-04-19T12:40:12Z",
  "editorVersion": "4.x",
  "os": "Windows 11 10.0.26200",
  "results": [
    { "setId": "creating-operators", "stepIndex": 0, "outcome": "pass", "comment": null, "timestampUtc": "..." },
    { "setId": "creating-operators", "stepIndex": 1, "outcome": "fail", "comment": "Symbol browser didn't focus search field", "timestampUtc": "..." }
  ]
}
```

### Markdown

Grouped by set:

```
## Creating Operators — 2 pass, 1 fail
- ✓ Open the Symbol Browser
- ✗ Search for "RG" — Symbol browser didn't focus search field
- ✓ Create a RadialGradient operator
```

### GitHub issue URL

`https://github.com/<owner>/<repo>/issues/new?title=Manual+test+run+<date>&body=<urlencoded markdown>`

Repo comes from git remote origin detected at startup. If detection fails, fall back to the clipboard path only.

## Rollout order

1. **Phase 1** — Parser + Pick state + Run state with local-only outcome capture. Summary shows in-memory only; export buttons stubbed.
2. **Phase 2** — JSON + Markdown clipboard export.
3. **Phase 3** — GitHub issue pre-fill URL.
4. **Phase 4** — Polish: related-help deep links, tag filter UX, Esc-to-abandon confirmation, keyboard shortcuts for Pass/Fail/Other.

## Testing (of the runner itself)

Manual test set for the runner lives at `.tests-manual/manual-test-runner.md` once Phase 1 ships. Eating our own dog food.

## Open questions (parking)

- Should the runner be gated behind a developer-mode flag, or always available under the Help menu? Lean "always available" — community testers need it.
- Do we want an explicit "Skip" outcome, or does not answering and pressing `→` suffice? Figma shows only three buttons; add Skip only if needed.
- Screenshot capture on Fail? Useful, but pulls in a capture dependency. Defer.
