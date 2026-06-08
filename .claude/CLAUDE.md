# Claude Code Instructions for TiXL

## Key References

**First action on any code task:** `Read .agentic/AGENT_INSTRUCTIONS.md`. Do this before planning, exploring, or editing — not after. The file is short (~200 lines) and it's the canonical source for performance rules, naming, ordering, and style. Skipping this step has produced repeated preventable corrections. If you've already read it in a recent turn of the current conversation, you may skip; across sessions, always re-read.

- `.agentic/AGENT_INSTRUCTIONS.md` -- coding conventions, performance rules, operator guidelines, style and formatting.
- `.agentic/SOLUTION_OVERVIEW.md` -- architecture map, dependency flow, task-oriented navigation
- `.agentic/Plans/` -- implementation plans for upcoming work (automatic tests, undo/redo coverage, timeline refactoring)
- `.tests-manual/` -- manual test sets (step-by-step walkthroughs for humans). See [`.tests-manual/README.md`](../.tests-manual/README.md) for format and process.

## Git Rules

- **Always use the `main` branch.** Never use `master`. The default remote branch is `origin/master` but local work happens on `main`.
- **Never use git worktrees.** They break Rider builds and cause permission issues. Always work directly on the main checkout's active branch.
- **Never use `git worktree add`**, `EnterWorktree`, or any worktree-related commands.

## Build Verification

- After any code change, run `dotnet build` on the affected project before reporting done.
- Check for build errors and fix them before proceeding.

## Project Conventions

- This is a C# / .NET 9 / DirectX 11 project using ImGui.NET for UI
- No heap allocations in per-frame code paths (no LINQ in hot loops, no closures, prefer simple for-loops)
- Use `UiColor`/`UiColors` helpers instead of hard-coded color values
- Store references by `Guid`, not by direct object reference
- Prefer editing existing files over creating new ones
- **Never reference a plan in a code comment** — no `.agentic/Plans/` paths, `Plan_*.md` filenames, or "see the plan / open question #N". Plans get archived and rewritten; the pointer rots. State the lasting *why* inline. (Agent-neutral rule; full version under "Comment Restraint" in AGENT_INSTRUCTIONS.)

## Documentation, Tests, and Background

These rules are agent-neutral and live in [`.agentic/AGENT_INSTRUCTIONS.md`](../.agentic/AGENT_INSTRUCTIONS.md) so non-Claude tooling (Codex, etc.) finds them too. Specifically:

- `.help/` rules, style, and the "capture informal knowledge" habit
- `.tests-manual/` rules for adding / updating manual test sets alongside feature PRs
- TiXL vs. Tooll3 background

Read that file.
