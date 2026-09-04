# Copilot Instructions for TiXL

Use this file as the default instruction source for work in this repository.

## Scope and Priority

1. Follow repository conventions and existing local patterns.
2. Prefer small, focused diffs over broad refactors.
3. Preserve behavior unless a task explicitly changes it.

## Architecture Orientation

- `Core/`: shared runtime model, evaluation, resources, and engine logic
- `Editor/`: authoring UI, interactions, graph editing, and tools
- `Player/`: exported runtime playback host
- `Operators/*`: operator packages and effect/content behavior

For detailed architecture and task routing, see `.agentic/SOLUTION_OVERVIEW.md`.
For full conventions, see `.agentic/AGENT_INSTRUCTIONS.md`; for driving the running
editor programmatically (graphs, parameters, screenshots, visual tests), see
`.agentic/DEBUG_PROTOCOL.md`.

## Performance Rules (Critical)

For methods called every frame (operator update, editor draw/update, hot rendering paths):

- Avoid heap allocations
- Avoid LINQ
- Prefer explicit loops and reusable buffers

Allocations are acceptable for explicit user-triggered actions.

## Resource and State Rules

- Avoid storing long-lived direct instance/resource references when a stable id exists
- Prefer storing/resolving by `Guid` where applicable
- Be careful with stale references after reloads/package changes

## UI Rules (Editor)

- Use `UiColor` / `UiColors` instead of hard-coded `float4` colors
- Use fonts sparingly, usually `Normal` and `Small`
- Prefer `CustomComponents` and `FormInputs` helpers for ImGui layout/input tasks

## Operator Rules

- Follow: https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions
- Keep operator evaluation paths allocation-free
- Match existing naming and slot conventions

## Formatting and Style

- Put `return` on a separate line (not inline after `if`)
- Place private fields and private enums at class end
- Prefix private fields with `_`
- Prefer clear variable names (`faceIndex` over `i` when clarity helps)

## Line Endings (Important for Bulk Edits)

The repo has **mixed line endings**: most `.cs` files use CRLF, some are LF.
There is no `.gitattributes` enforcing a single convention and `core.autocrlf`
is `false`. When writing scripts that batch-rewrite many files this is the
single biggest source of noisy diffs.

Rules for any bulk-edit script (Python, sed, etc.):

1. Read files in **binary mode** (`open(path, 'rb')`). Do NOT use `read_text` or
   text-mode reads — they silently strip `\r`.
2. **Detect each file's existing line ending** before writing. If the file
   contains `\r\n`, write CRLF; otherwise LF. Per-file, not per-repo.
3. Write in **binary mode** (`open(path, 'wb')`) with the bytes you produced.
4. Sanity-check with `git diff --shortstat` before committing. If the line
   count is much higher than the logic change implies, line endings were
   munged — fix the working tree and `commit --amend`.

## Review and Quality Expectations

- Point out obvious bugs, misleading code, incorrect implementations, and typos
- Fix spelling mistakes in touched comments when editing nearby code
- Add parameter documentation only when purpose is not obvious from the parameter name

## Working Checklist

Before editing:

- Identify whether the code path runs every frame
- Check nearby files for local conventions
- Confirm the right project boundary (`Core`, `Editor`, `Player`, `Operators`)

After editing:

- Validate impacted files/build where practical
- Call out performance impact and residual risks

## References

- Main docs: https://github.com/tixl3d/tixl/wiki
- How TiXL works: https://github.com/tixl3d/tixl/wiki/help.HowTixlWorks
- Existing local guidance: `AGENT_INSTRUCTIONS.md`
- Architectural map: `SOLUTION_OVERVIEW.md`

