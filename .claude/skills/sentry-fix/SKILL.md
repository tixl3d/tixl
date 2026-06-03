---
name: sentry-fix
description: Walk through open Sentry issues for tooll3, latest first, proposing a fix and committing each one separately with user review between. Use when the user wants to triage Sentry, fix Sentry issues, work through the Sentry backlog, or invokes /sentry-fix.
---

# sentry-fix

Drive a review-gated loop over open Sentry issues for the `tooll/tooll3` project. One commit per issue, user reviews between each.

## Prerequisites — check before doing anything else

1. **Token present.** Read `.env` at repo root. If `SENTRY_AUTH_TOKEN=` is empty or the file does not exist, stop and tell the user:
   - Create a token at https://sentry.io/settings/account/api/auth-tokens/ with **both** `event:read` and `event:write` scopes. `event:read` covers listing/fetching; `event:write` is required to mark issues resolved at hand-off.
   - Copy `.env.example` to `.env` and paste the token in.
   - Then re-invoke the skill.
2. **Helper script reachable.** Confirm `Scripts/sentry-issues.ps1` exists. If not, the skill is incomplete — tell the user.

If a `-Resolve` call returns `403 Forbidden`, the token is read-only — tell the user to regenerate it with `event:write` (Sentry doesn't allow editing scopes after creation).

Do not store the token, do not echo it, do not print it.

## Step 1 — list open issues

Run:

```powershell
.\Scripts\sentry-issues.ps1 -List -Limit 25
```

That returns slimmed JSON, latest-`lastSeen` first. Parse it.

Show the user a compact table — short ID, title, level, count, lastSeen — and ask them to confirm they want to start with #1, or pick a different issue / skip filter. Do not auto-start without a confirm; some issues may be already-known noise.

If the list is empty, say so and stop.

## Step 2 — per-issue loop

For the chosen issue:

### 2a. Fetch details

```powershell
.\Scripts\sentry-issues.ps1 -Issue <numericId>
```

This returns `{ issue, event }`. The interesting bits:
- `issue.title`, `issue.culprit`, `issue.metadata`
- `event.entries[?type=='exception'].data.values[*].stacktrace.frames` — frames with `filename`, `function`, `lineNo`, `inApp`
- `event.tags` — release, environment, os, runtime
- `event.contexts` — useful runtime/device context

### 2b. Map stack frames to repo files

Sentry stack-trace filenames look like `D:\a\tixl\tixl\Core\Operator\Symbol.cs` (CI build path) or relative like `Gui\Windows\SymbolLib\NamespaceTreeNode.cs`. To resolve:
- Strip the CI prefix (`D:\a\tixl\tixl\`) — what remains is repo-relative.
- Convert `\` to `/` if needed for the Read tool.
- The `lineNo` from Sentry is 1-based and lines up with the file in `main`.

Read the named function plus ~20 lines of surrounding context. Read every `inApp: true` frame from the top of the stack until you've understood the trigger. Stop reading once you have enough to explain the cause — don't open every frame in the trace.

### 2c. Analyze and explain

Write a short root-cause statement to the user — 2-4 sentences. State which null reference, which invariant was broken, which input was unexpected. Cite the exact `file:line` you read. If you genuinely can't determine the cause from the trace (missing context, intermittent timing bug, third-party code), say so and ask if the user wants to skip or annotate the issue in Sentry instead of fixing blind.

### 2d. Propose the fix

Describe the fix in 1-3 bullets before editing. Mention:
- What you'll change and where (file:line).
- Why it's the right level of fix vs. a deeper refactor (defer the refactor unless trivial).
- Risk: are other call sites affected?

Wait for user "go" / "yes" / "do it" — or pushback. Do not edit without an ack.

### 2e. Apply

Edit minimally. Follow `.agentic/AGENT_INSTRUCTIONS.md` rules:
- No allocations / LINQ in per-frame paths.
- Match nearby style; CRLF line endings for `.cs` files unless the file is LF.
- Don't refactor or "tidy up" surrounding code.

### 2f. Build the affected project

Identify the project from the changed file's path (Core, Editor, Operators/Lib, etc.) and run:

```powershell
dotnet build <project>.csproj
```

If the build fails, fix the build error before continuing. Do not hand off a broken build for review.

### 2g. Hand off for review — do not commit

**The user commits the change themselves**, so they're forced to review every diff before it lands. After the build is green:

1. Run `git status --short` and `git diff --stat` so the user sees what they're reviewing.
2. Draft a suggested commit message in a fenced block (don't run `git commit`). Format:

   ```
   <area>: <one-line fix summary>

   Fixes Sentry tooll3 issue <SHORT-ID> (<permalink>).
   <2-4 lines: what was happening, what the fix does, any caveats.>
   ```

   Examples of `<area>`: `Core`, `Editor`, `Operators`, `Gui`. Match the project root the file lives in. Do **not** include `Co-Authored-By` unless the user has asked for it project-wide — check `git log` for the recent convention.
3. State the next issue from the list (short-ID + title) so the user knows what's queued.

### 2h. Pause for the user

**Stop and wait.** Do not run `git commit`, `git add`, or move to the next issue. End the hand-off message with the question:

> **Resolve Sentry `<SHORT-ID>` as fixed-in-next-release?** Reply `next` to mark resolved and continue, `next nores` to continue without resolving, or `skip` / `revert` / `stop`.

User reply mapping:

- `next` / `yes` / `continue` — first run `Scripts\sentry-issues.ps1 -Resolve <numericId> -InNextRelease`, then proceed to the next issue (step 2a). Sentry auto-resolves on the next release tag and auto-reopens as a regression if it recurs.
- `next nores` / `next no` — proceed to the next issue without resolving. Use this when the user committed but isn't confident enough to mark resolved (e.g. wants to manually verify first).
- `skip` — drop the current next-issue, present the one after. Don't resolve.
- `revert` / `undo` — they've reverted the working-tree edit themselves; re-examine or move on. Don't resolve.
- `stop` — end the session. Don't resolve.

If the user only says `next` without any explicit yes/no on resolution, treat it as the default (`next` = resolve). If they previously said `next nores` for some issues, don't assume that sticks for the next one — re-ask each time.

If the user says nothing or asks a question, answer the question and stay paused.

## Won't-fix items (Archive instead of Resolve)

Some issues aren't fixable from TiXL's code: driver bugs (DXGI E_NOTIMPL), library version mismatches we don't control, user environment problems (missing .NET runtime on the target machine in a way TiXL can't detect), broken installs we can't recover. For these, mark the issue archived on Sentry instead of resolved-in-next-release:

```powershell
.\Scripts\sentry-issues.ps1 -Archive <numericId>
```

Sentry hides archived issues from the default unresolved view. They auto-reopen if Sentry's "escalation" detector decides the issue has become significant again (e.g. starts hitting a much wider user base). The right surface for "won't fix from our side."

When triaging, propose `Archive` rather than `Resolve` for clearly out-of-our-hands cases, and let the user confirm.

## Anti-patterns — do not

- **Do not run `git commit` or `git add`** — the user commits themselves so they're forced to review every diff.
- Do not batch multiple issues' edits into one set of working-tree changes — fix one, hand off, wait, then start the next.
- Do not push (`git push`).
- Do not resolve an issue on Sentry without the user's explicit `next` / `yes` at step 2h. The hand-off question is the trigger; silence is not consent.
- Do not `Resolve` an issue we didn't actually fix — that falsely claims a fix landed and will mark it as a regression when it recurs. Use `Archive` for won't-fix.
- Do not invent stack-trace lines that aren't in the returned event payload. If a frame says `(12 additional frames were not displayed)` and you need them, ask the user to open the issue in the Sentry web UI and paste the full trace.
- Do not use `git worktree`. The project explicitly forbids worktrees (see `.claude/CLAUDE.md`).

## When to bail

Stop the loop and report up if:
- The build is broken before you started (unrelated to any Sentry issue).
- You hit a Sentry API error you can't interpret (the helper script will exit non-zero with a message on stderr).
- An issue's root cause clearly spans more than one fix (an architectural problem). Say so, suggest a separate plan in `.agentic/Plans/`, and move on or stop.
