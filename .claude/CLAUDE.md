# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Key References

**First action on any code task:** `Read .agentic/AGENT_INSTRUCTIONS.md`. Do this before planning, exploring, or editing — not after. The file is long (~580 lines) but it's the canonical source for performance rules, naming, ordering, and style. Skipping this step has produced repeated preventable corrections. If you've already read it in a recent turn of the current conversation, you may skip; across sessions, always re-read.

- `.agentic/AGENT_INSTRUCTIONS.md` -- coding conventions, performance rules, operator guidelines, style and formatting.
- `.agentic/SOLUTION_OVERVIEW.md` -- architecture map, dependency flow, task-oriented navigation
- `.agentic/DEBUG_PROTOCOL.md` -- TCP debug bridge for driving the editor (graphs, params, screenshots, visual tests)
- `.agentic/BISECT_PROTOCOL.md` -- bisecting runtime regressions across editor versions (roles, per-commit loop, injected measure probe, pitfalls)
- `.agentic/DOCUMENTATION_ECOSYSTEM.md` -- map of the `.help/` docs system (help surfaces by depth, index schema, cross-references, the video-transcript-to-docs pipeline)
- `.agentic/Reviews/FEATURE_REVIEW.md` -- feature review procedure (`/review-tixl-feature` is a stub pointing here)
- `.agentic/Plans/` -- implementation plans for upcoming work (automatic tests, undo/redo coverage)
- `.tests-manual/` -- manual test sets (step-by-step walkthroughs for humans). See [`.tests-manual/README.md`](../.tests-manual/README.md) for format and process.

## Git Rules

- **Always use the `main` branch.** Never use `master`. The default remote branch is `origin/master` but local work happens on `main`.
- **Never use git worktrees.** They break Rider builds and cause permission issues. Always work directly on the main checkout's active branch.
- **Never use `git worktree add`**, `EnterWorktree`, or any worktree-related commands.

## Build Verification

- After any code change, run `dotnet build` on the affected project before reporting done.
- Check for build errors and fix them before proceeding.

## Commands

Solution is `t3.sln`; SDK pinned by `global.json` (.NET 10, `latestMinor`). CI (`.github/actions/build`) runs `dotnet restore` + `dotnet build --configuration Release` at the root.

```sh
dotnet build Editor/Editor.csproj                     # editor (pulls in Core, Graphics, operator packages)
dotnet test Core.Tests/Core.Tests.csproj              # xunit unit tests (curves, serialization, graphics compat, laser...)
dotnet test Core.Tests --filter "FullyQualifiedName~CurveSerializationTests"   # single class / test
dotnet test VideoServices.Tests
dotnet run --project Tests/CollabHeadless             # console harness for the collab network layer (not xunit)
```

`Tests/Editor.IntegrationTests` (xunit, serialized) drives a real editor over the debug protocol: it attaches to a running editor on `TIXL_DEBUG_PORT` (default 9042) or launches `TiXL.exe --debug-server <port>` itself, and works in the `_agentTests` project. See `.agentic/DEBUG_PROTOCOL.md`.

## Architecture Beyond SOLUTION_OVERVIEW

`SOLUTION_OVERVIEW.md` predates these projects:

- `Graphics/` — backend-agnostic GPU interface (`IGraphicsBackend`), shaped after Vulkan (explicit usage, baked pipeline state, command lists, frame begin/end). `Graphics.D3D11/` and `Graphics.Vulkan/` implement it. `Graphics.Compat/` translates the D3D11-style calls that existing Core/operator code makes into it; new code should call `Graphics` directly.
- `SdlPlatform/` — SDL windowing, input, dialogs and ImGui renderer (cross-platform alternative to the Windows/Silk hosts).
- `CollabRelay/` — standalone relay server for live-collaboration rooms; `Tests/CollabHeadless` compile-includes the production network sources and doubles as the POSIX compile gate.
- `Spikes/` — throwaway experiments; not product code.

## Project Conventions

- This is a C# / .NET 10 project (DirectX 11 today, Vulkan backend in progress) using ImGui.NET for UI
- No heap allocations in per-frame code paths (no LINQ in hot loops, no closures, prefer simple for-loops)
- Use `UiColor`/`UiColors` helpers instead of hard-coded color values
- Store references by `Guid`, not by direct object reference
- Prefer editing existing files over creating new ones
- **Never reference a plan in a code comment** — no `.agentic/Plans/` paths, `Plan_*.md` filenames, or "see the plan / open question #N". Plans get archived and rewritten; the pointer rots. State the lasting *why* inline. (Agent-neutral rule; full version under "Comment Restraint" in AGENT_INSTRUCTIONS.)

## Line Endings

The repo is LF-only: `.gitattributes` normalizes (`* text=auto eol=lf`) and `.editorconfig` enforces LF, matching `.agentic/AGENT_INSTRUCTIONS.md`. Bulk-edit scripts must write `\n` only — text-mode reads silently turn LF into CRLF on Windows and turn a small change into a whole-file diff. Sanity-check with `git diff --shortstat` before committing.

## Documentation, Tests, and Background

These rules are agent-neutral and live in [`.agentic/AGENT_INSTRUCTIONS.md`](../.agentic/AGENT_INSTRUCTIONS.md) so non-Claude tooling (Codex, etc.) finds them too. Specifically:

- `.help/` rules, style, and the "capture informal knowledge" habit
- `.tests-manual/` rules for adding / updating manual test sets alongside feature PRs
- TiXL vs. Tooll3 background

Read that file.
