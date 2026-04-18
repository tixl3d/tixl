# TiXL Help — Writing Style

These are the docs that ship with the project and publish to `tixl3d.github.io`. They're the **single source of truth**: the GitHub wiki is now a redirect.

## Voice and length

- Write for someone learning TiXL, not someone proving they already know it. Plain English, short sentences, second person.
- A page should answer one question or explain one feature area. If it needs a table of contents, it should probably be two pages.
- Aim for **150 – 400 lines** per page. Under 50 lines is often a sign the topic should be merged into a parent page.
- No marketing tone. No walls of warning callouts. If something is dangerous, say so once and move on.

## Structure

- **H1**: the page title (matches the filename, without the `.md`).
- **First paragraph**: one or two sentences that tell the reader what the page covers and when they'd want it.
- **H2** sections for the main beats. Use `## Heading` — no numbering, no emojis.
- **H3** only when an H2 section has internally distinct steps. Three-deep is the max.
- **Lists** when order matters (steps) or items are parallel (options). Otherwise, prose.
- End with a **See also** section linking to related pages when it helps — not because every page needs one.

## Links

- Use **relative paths** with the `.md` extension inside `.help/`:
  ```md
  [Installation](../setup/Installation.md)
  [Timeline](TimeLine.md)
  ```
- External links get full URLs. No bare `[text](https://...)` — introduce the link with context.
- Avoid linking the same term more than once per page.

## Images

- Store next to the page that uses them: `.help/ui/images/timeline-sri.png`.
- **Max width 1600 px**, PNG for screenshots, JPEG only for photographic content. Compress before committing — screenshots should be well under 500 KB.
- Filenames: lowercase, kebab-case, descriptive. `timeline-sri-hover.png`, not `screenshot-3.png`.
- Reference with a relative path and descriptive alt text:
  ```md
  ![SelectionRangeIndicator hovered](images/timeline-sri-hover.png)
  ```

## Code and UI references

- Fenced code blocks with a language tag: ` ```csharp `, ` ```hlsl `, ` ```bash `.
- UI elements in **bold** the first time, plain after: "Press **Alt+Click** to …".
- File and operator names in inline code: `` `SelectionRangeIndicator` ``, `` `[Value]` ``.
- Keyboard shortcuts: `Ctrl+Shift+S`, not `CTRL + SHIFT + S` or `⌃⇧S`.

## Staying in sync with code

- **When you ship a user-visible UI or behavior change, update the matching page** in the same PR. The agent instructions in `.claude/CLAUDE.md` enforce this.
- If a feature is removed, remove the doc section (don't leave it marked "deprecated" indefinitely — git history is the deprecation log).
- If a page describes behavior that has drifted, **flag it in [../.agentic/Plans/Plan_UpdateHelp.md](../.agentic/Plans/Plan_UpdateHelp.md)** rather than silently leaving it wrong.

## What not to write

- Internal implementation details (class names, private methods). Those belong in code comments or `.agentic/SOLUTION_OVERVIEW.md`.
- Release notes or changelogs — those live elsewhere.
- Personal opinions, TODO lists, "I'm not sure but…". If you're not sure, don't commit the page.

## File naming

- `PascalCase.md`, matching the page title roughly. No `help.` / `dev.` / section prefixes (the folder already provides the section).
- Examples: `Introduction.md`, `UsingCustomShaders.md`, `TimeLine.md`.
