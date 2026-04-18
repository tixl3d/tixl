# Claude Code Instructions for TiXL

## Key References

- `.agentic/AGENT_INSTRUCTIONS.md` -- coding conventions, performance rules, operator guidelines, style and formatting. **Read this before making any code changes.**
- `.agentic/SOLUTION_OVERVIEW.md` -- architecture map, dependency flow, task-oriented navigation
- `.agentic/Plans/` -- implementation plans for upcoming work (automatic tests, undo/redo coverage, timeline refactoring)

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

## Background and Documentation

- **TiXL is the current product** (v4.x). **Tooll3** (v3.x) is the legacy predecessor — a large portion of v4 is a rewrite. Don't write new docs or features targeting Tooll3; treat remaining Tooll3 references in code as historical and prefer removing them over updating them unless there's a concrete migration use case.
- User-facing documentation lives in `.help/` and publishes to `tixl.app/help/`. Developer / contributor topics stay on the GitHub wiki. Don't mix the two. See [`.help/STYLE.md`](../.help/STYLE.md) for writing conventions and [`.agentic/Plans/Plan_UpdateHelp.md`](../.agentic/Plans/Plan_UpdateHelp.md) for the overall docs plan.
- When shipping a user-visible UI or behavior change, update the matching page under `.help/` in the same PR. If no suitable page exists, add one under the best-fitting section (`getting-started/`, `install/`, `using/`, `advanced/`, `contributing/`), or flag it in `Plan_UpdateHelp.md`.
- **Capture informal knowledge.** When the user explains something, shares a Discord thread, describes how they answered someone's question, or shows you a meet-up clip — ask whether it belongs in `.help/`. If it's broadly useful, offer to draft a paragraph or a page from it. Small additions compound; the goal is docs people can find answers in instead of re-asking. Raw source material (scripts, transcripts) lives in `.help/.src/` and isn't published.
