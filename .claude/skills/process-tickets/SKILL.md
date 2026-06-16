---
name: process-tickets
description: Unattended batch over the "In progress" column of the TiXL road-map GitHub board. Implements straightforward tickets as individual git stashes and writes plans for the rest as files in .agentic/Plans/ — no questions, no commits, no GitHub writes. Use when the user wants to work through their In-progress tickets, process the board backlog, or invokes /process-tickets.
---

# process-tickets

Work the **In progress** column of the TiXL road-map board (org `tixl3d`, project 3,
"TiXL road map") unattended. Each ticket produces one reviewable artifact: a **git stash** for an
implemented code change, or a **plan file** in `.agentic/Plans/` for everything else. The user sifts
these later at their leisure.

Why the split: git GUIs (e.g. git Fork) **do not show untracked files stored inside a stash**, so a
stashed new plan doc looks empty in review — useless. Code edits are tracked-file diffs and show fine
in a stash; new plan files belong in the working tree, where they appear under *Local Changes* and get
committed to `.agentic/Plans/`.

This is the user's intentionally messy inbox: they collect ideas as **In progress**, and
push anything too involved to **Todo** / a later milestone themselves. So most tickets are
small; the rest should become a plan, not a guess.

## Hard invariants — never violate these

- **Read-only on GitHub.** Never comment, close, label, move cards, or edit issues/the board.
  The helper script only reads. There is no write path and you must not add one.
- **No commits, no `git add`, no `git push`.** The user reviews and commits everything
  themselves. Touching the index or committing defeats the whole workflow.
- **Only ever *create* stashes. Never `pop`, `apply`, or `drop` an existing stash.** The
  user keeps live WIP in stashes (e.g. `keep/...`); destroying one loses real work.
- **Run unattended — do not ask the user questions mid-run.** That's the point: they fire
  it and walk away. Resolve every "should I…?" by the classifier below, biasing to *plan*.
- **Code changes → one stash each** (return to clean `main` between them, so each stash is an
  independent diff). **Plan files → left in the working tree, never stashed.**

## Prerequisites — check before doing anything else

1. **`read:project` scope.** Run `.\Scripts\board-tickets.ps1 -List`. If it prints an
   `INSUFFICIENT_SCOPES` hint, stop and tell the user to run `gh auth refresh -s read:project`,
   then re-invoke. (Projects v2 is GraphQL-only; a public board still needs the token scope.)
2. **Helper script present.** `Scripts/board-tickets.ps1` must exist. If not, the skill is
   incomplete — tell the user.
3. **Conventions loaded.** Read `.agentic/AGENT_INSTRUCTIONS.md` if you haven't this session.
   Every code edit must follow it (no per-frame allocations/LINQ, `UiColors.*`, `Guid` over
   direct refs, `* T3Ui.UiScaleFactor` on pixel literals, brace every branch, CRLF unless the
   file is LF, minimal scoped diffs, no plan references in comments).

Parsing the helper output: use the **argument form** `ConvertFrom-Json (.\Scripts\board-tickets.ps1 -List)`,
not the pipe form — Windows PowerShell 5.1's `ConvertFrom-Json` mis-handles a piped JSON array.

## Step 0 — protect the user's current work

Run `git status --short`. If the working tree is dirty, the user has uncommitted work; an
unattended run must not lose it. Stash it first under a clearly-marked name:

```powershell
git stash push -u -m "keep/before-ticket-run"
```

Note in the final report that you did this so the user pops it back. If the tree is clean,
proceed. Never drop or pop any pre-existing stash.

## Step 1 — list the queue

```powershell
$tickets = ConvertFrom-Json (.\Scripts\board-tickets.ps1 -List)
```

Each record has: `number`, `title`, `url`, `repo`, `size` (S/M/L/XL/XXL or empty),
`milestone`, `labels`, `body`.

Build the **already-processed set** from two places (re-runs are idempotent):
- **Code**: `git stash list` — every ticket number appearing as a `ticket #<n>` marker.
- **Plans**: `.agentic/Plans/` — any file (tracked or not) whose body has a `Ticket: #<n>` line.

Skip a ticket if it shows up in either.

**Every ticket leaves an artifact — including non-actionable ones.** A board test stub (empty
body + trivial title like "Test"), an infeasible request, or one that only raises questions still
gets a plan file saying exactly that (see 2c-iii). No silent skips — that keeps the record complete
and re-runs idempotent.

## Step 2 — per-ticket loop (no prompts)

For each remaining ticket, in board order:

### 2a. Read it
Use the `body` from the list (or re-fetch with `-Ticket <n>`). Understand what's actually
being asked. Locate the relevant code with Grep/Glob/Read (or an Explore agent for wide
searches). Don't start editing until you know which files are in play.

### 2b. Classify — implement now, or write a plan?

**Implement now** only when *all* hold:
- The requirement is unambiguous — one obvious correct behavior, no design decision to make.
- The change is localized (roughly ≤ 3 files) and additive/low-risk.
- Size is S/M (or unset but clearly small).
- No data-model / serialization / saved-project / migration impact.
- No operator rename/removal or input-set change (those break user projects).
- For UI tickets: the **visual and interaction design is already determined** by the ticket
  or by an obvious existing pattern. Per AGENT_INSTRUCTIONS, UI design is not something to
  guess unattended — if the look or interaction is open, it's a plan.

**Write a plan** when *any* hold:
- Size L/XL/XXL, or the change is cross-cutting / architectural.
- Touches `Symbol`/serialization/data model/migration, or renames/removes operators or params.
- The requirement is vague, exploratory ("Idea: …"), or needs a design/UX decision.
- The build can't be made green with a minimal, confident change.

When genuinely unsure, **write a plan** — a discarded plan costs seconds; broken unattended
code costs an untangling session.

### 2c-i. If implementing
- Edit minimally, following `.agentic/AGENT_INSTRUCTIONS.md`. No opportunistic refactors.
- Build the affected project (infer it from the changed path — `Core`, `Editor`,
  `Operators/Lib`, …):
  ```powershell
  dotnet build <project>.csproj
  ```
- **Green** → stash it (2d).
- **Can't go green** with a minimal fix → revert your edits (`git restore <files>` for
  tracked files; delete any new files you created), and fall through to a plan (2c-ii)
  instead. Never stash broken code.

### 2c-ii. If planning
Write `.agentic/Plans/Plan_<PascalCaseSlug>.md` (slug from the title) and **leave it in the working
tree — do not stash it.** It then appears under *Local Changes* for the user to review and commit to
`.agentic/Plans/`. The `Ticket: #<number>` line below is what the next run greps to skip it. Keep it
concise:

```markdown
# <Title>

Ticket: #<number> — <url>
Size: <size>   Milestone: <milestone>

## Problem
<1-3 sentences: what the ticket wants and why it isn't a trivial edit.>

## Affected code
<files / systems involved, with paths.>

## Proposed approach
<the plan in a few bullets.>

## Risks / side-effects
<serialization, migration, perf, UX, cross-feature overlap.>

## Open questions
<decisions the user must make before this can be implemented.>
```

Do **not** reference this plan path from any source-code comment (AGENT_INSTRUCTIONS).

### 2c-iii. If not actionable (still write a plan)
A ticket that is a test stub, infeasible, or only raises questions you can't resolve
unattended **still gets a plan file** (in the working tree, not stashed) — never a contentless skip.
Write the same file, but the body states the situation honestly:
- **Test stub / no content** → "Appears to be a placeholder; no work taken. Add a description
  to have it picked up next run."
- **Infeasible** → why it can't be done as asked, and what would have to change first.
- **Needs clarification** → the specific questions that block it, under `## Open questions`.

This guarantees every ticket produces exactly one artifact, so nothing is silently dropped.

### 2d. Stash implemented code changes (code tickets only)
Only **implemented** tickets get stashed — plan files (2c-ii / 2c-iii) stay in the working tree.
Stash **only this ticket's edited files** with a pathspec (`-- <paths>`), not a blanket `git stash
push -u`, so each stash is exactly one ticket — clean to apply → review → delete in a git GUI (e.g.
git Fork), with no stray file bundled in. New tracked files an implementation adds (e.g. a new
operator) are tracked-diffs and show fine in the stash; include their paths too.

```powershell
git stash push -m "ticket #<number>: <short title>" -- <file1> <file2>
```

After stashing, the tracked tree is back at clean `main` for the next code ticket. Plan files from
earlier tickets may sit untracked in the tree — that's fine: the pathspec stash ignores them and they
don't affect builds.

### 2e. Record the outcome
Track, per ticket: number, title, disposition (`implemented` / `planned` / `not-actionable` /
`build-failed→planned`), stash marker, files touched, and a one-line note.

## Step 3 — final report

After the queue is done, print a single summary table:

| # | Title | Disposition | Artifact | Files | Note |

Artifact = a `stash@{…}` marker for code, or the `.agentic/Plans/Plan_*.md` path for a plan.

Then:
- Flag **overlapping files** — e.g. a plan that will later edit the same file a code stash already
  changed — and suggest an order (land the code stash first).
- Remind the user how to review each kind:
  - **Code stashes**: `git stash show -p stash@{n}`; or `git stash apply stash@{n}`, review, commit.
  - **Plan files**: already in *Local Changes* / `.agentic/Plans/` — read, commit the keepers, delete
    the rest. (No stash to pop — they're plain files.)
- If Step 0 stashed their pre-run work as `keep/before-ticket-run`, remind them to pop it.

## Optional arguments

- **Triage only** (no edits): if the user asks to "just triage" / "dry run", do Steps 1–2b
  only — classify every ticket and print the table with the *intended* disposition, writing
  nothing and creating no stashes. Good for a first look before a real run.
- **Single ticket**: if the user names a ticket number, process only that one.
- **Different column**: pass `-Status Todo` (etc.) to the helper to work another column.
