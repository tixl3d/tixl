# Style conventions

TiXL is written by many people, and its texts end up in many places: manual pages, wiki pages, operator descriptions, menu labels, release notes and code. This page gathers the conventions for each, so contributions read as if one careful person wrote them. Where a longer guide exists, this page summarizes it and links to it.

## Manual pages

The full guide is [Writing style for these docs](../STYLE.md). The essentials:

- **Write for someone learning TiXL.** Plain English, short sentences, second person ("you"). No marketing tone.
- **One page answers one question** or explains one feature area. Aim for 150–400 lines; split pages that need a table of contents.
- **Start with what the page covers** and when the reader needs it, in one or two sentences under the title.
- **Use `##` sections**, `###` only for distinct steps inside a section. No numbering or emojis in headings.
- **Link with relative paths** including the `.md` extension, such as `[Timeline](../using/Timeline.md)`. Introduce external links with some context.
- **Write operator names in square brackets**, such as `[AdjustColors]`. The build turns them into links to the operator reference. Don't wrap them in code formatting.
- **Format consistently:** UI elements in **bold** the first time, file and class names in `code`, shortcuts as `Ctrl+Shift+S`.
- **Screenshots:** PNG, at most 1600 px wide and well under 500 KB, with lowercase, descriptive file names (`timeline-clip-handles.png`) and descriptive alt text.
- **File names** are `PascalCase.md`, without `help.` or `dev.` prefixes.

## Wiki

The [GitHub wiki](https://github.com/tixl3d/tixl/wiki) is for developer and contributor topics and the meet-up notes. Everything a TiXL user needs goes into the manual instead.

- **Page names:** `dev.PascalCase` for developer pages, such as `dev.WorkingWithGit`. No hyphens or spaces. Meet-up notes are `meetup.YYYYMMDD` and are generated, so edit them only to fix mistakes.
- **Don't edit `help.*` pages.** They are redirects to the manual and their content is no longer maintained. Make the change in `.help/docs/` instead.
- **Check before you create.** Search the wiki for a page on the same topic and extend it rather than adding a second one.
- **No operator pages.** Operator documentation comes from the descriptions in the editor (see below).
- **Add new pages to the sidebar** (`_Sidebar.md`) in the matching section, or nobody will find them.
- **Links** use the page name without extension: `[Coding conventions](dev.CodingConventions)`.
- **Images** go into a folder named after the page, such as `dev.OperatorConventions/anim.gif`.
- **Plain Markdown.** Avoid HTML such as `<br>` unless there is no other way.
- **Edit locally for anything bigger than a typo.** Clone `https://github.com/tixl3d/tixl.wiki.git` next to your `tixl` checkout and edit it in an editor with Markdown preview, such as VS Code or Rider. Commit and push like any other repository.

Apart from that, the writing rules of the manual apply.

## Operator descriptions

The full conventions are on the wiki under [Operator conventions](https://github.com/tixl3d/tixl/wiki/dev.OperatorConventions). The description is shown in the editor's help panel, in tooltips and search, and on the generated reference page.

- **The first line is a one-line summary** of what the operator does, and nothing else. Lists, search results and tooltips show only this line.
- **Leave a blank line** after the summary. Descriptions are Markdown, and a single line break does not start a new paragraph.
- **Further paragraphs** explain how it works and how it is typically combined. Write them in the third person ("Generates…", not "You can…").
- **List synonyms** on an `AKA:` line, so search finds the operator under other names.
- **Put subjective advice** under a `Tips` heading. Tips may address the reader directly.
- **Describe parameters** too, even briefly. A parameter description is often the only help someone reads.
- **Parameter layout:** group related parameters with a group title once an operator has more than a handful; give numeric parameters a `Min` and `Max` that cover the useful range; clamp only values that really break outside a range.
- **Edit descriptions in the editor**, or in the `.t3ui` file while the editor is closed. A running editor overwrites the file when it saves.

## Labels in the user interface

- **Commands use Title Case:** menu items, buttons and context-menu entries. Short articles, conjunctions and prepositions of up to four letters stay lowercase unless they come first or last: `Reset to Default`, `Sync with Selection`, `Combine into New Type...`. Particles of phrasal verbs are capitalized: `Set Up`, `Zoom In`.
- **A trailing `...`** means the command asks for more input before it acts, for example by opening a dialog. Use three periods, not the `…` character.
- **Prose uses sentence case:** tooltips, help texts, warnings and confirmation questions.

## Release notes

- Every user-visible change adds **one short bullet** to the release notes of the upcoming version in `.help/release-notes/v<major>.<minor>.md`, in the matching section.
- Describe what changed for the user, not how it was implemented.
- Only a major feature or a new concept gets its own section.

## Manual test sets

- A pull request that changes UI behavior adds or updates a test set in `.tests-manual/`.
- New sets include `added:` (the date) and `added-in-version:` in their front matter.
- Remove a test set together with the feature it covers.

The format is described in [`.tests-manual/README.md`](https://github.com/tixl3d/tixl/blob/main/.tests-manual/README.md).

## Code

The general conventions are on the wiki under [Coding conventions](https://github.com/tixl3d/tixl/wiki/dev.CodingConventions). The most current and detailed rules, including those for user interface code, are in [`AGENT_INSTRUCTIONS.md`](https://github.com/tixl3d/tixl/blob/main/.agentic/AGENT_INSTRUCTIONS.md). It is written for coding agents but applies to everyone. The most important points:

- **No heap allocations in code that runs every frame**: no LINQ, no closures, reuse buffers. Operator updates and editor drawing run at the display's refresh rate.
- **Every change to project data goes through an undoable command.**
- **Use the existing UI helpers** for colors, fonts, icons and widgets, and scale every pixel value by the UI scale factor.
- **Keep `Core/` changes minimal.** Put editor-only code in `Editor/`.
- **Comments explain why, not what**, and stay short. No references to issues, plans or dates in code comments.
- **Line endings are LF.**

## Commit messages

Start the subject with a short lowercase area prefix, then describe the change in the imperative: `ui: Fix double active line glitch in app menu`, `ops: Add [RenderToCubeMap]`. Common prefixes are `ui:`, `ops:`, `fix:`, `render:`, `help:`, `dev:` and `io:`.

## See also

- [Contributing](README.md)
- [Documentation structure](DocumentationStructure.md)
- [Writing style for these docs](../STYLE.md)
