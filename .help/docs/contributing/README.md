# Contributing

TiXL is an open-source project with a small core team. Almost every part of it gets better through people outside that team: a well-written bug report, a fixed typo, a new example, a missing operator description, a pull request. This section explains where help is needed, how the pieces fit together, and how to get your work merged.

You don't need to be a graphics programmer. Most of the valuable contributions need no C# at all.

## Why it matters

TiXL covers a lot of ground: hundreds of operators, a node editor, a timeline, live I/O, export, shaders. Nobody can test all of it on every graphics card, write every page or record every tutorial. What makes the difference is the number of people who notice something and do something about it.

Some contributions have a much larger effect than their size suggests:

- **A reproducible bug report** often saves hours of guessing. A report with steps, a screenshot and the log file is frequently fixed within days.
- **One paragraph in the docs** answers the same question for everyone who would otherwise ask it on Discord.
- **A good operator description or example** shows up in the editor itself, right where people get stuck.
- **A small, focused pull request** is easy to review and merge. A large one is not.

## Start by reporting

The easiest first contribution is telling us what doesn't work. Read [Reporting bugs and feature requests](../getting-started/ReportBugs.md) for what makes a good report, then open an issue on [GitHub](https://github.com/tixl3d/tixl/issues) or post in the Discord help channel.

Feature requests are welcome too. Describe the problem you want to solve rather than the button you'd like to have; that leaves room for a better solution.

## Work on the latest version

Once you go beyond reporting, **clone the current source from GitHub** instead of working against the last release. It is the single most useful habit for any contributor:

- The bug you are about to report may already be fixed on `main`.
- Docs, operator descriptions and examples have to match the current editor, not the one from three months ago.
- You can test fixes the day they land and tell us whether they work for you.
- Editing docs, operators and examples is easiest in a local checkout, where you can preview them and then open a pull request.

[Set up a development environment](../install/InstallDev.md) walks you through installing the .NET SDK and an IDE, cloning the repository and running TiXL from source. It takes about half an hour, and you don't need to write any code to benefit from it.

Then keep it current: `git pull` before you start something new, and rebuild.

## Ways to contribute

| Area | What helps | Skills needed | Where to start |
| --- | --- | --- | --- |
| Testing | Reproducible issues, testing pre-releases, running the manual test sets | Using TiXL | [Reporting bugs](../getting-started/ReportBugs.md) |
| Community | Answering questions on Discord, sharing projects, showing work at a meet-up | Using TiXL | [Community](../getting-started/Community.md) |
| Docs | Fixing errors, filling "Still to write" gaps, turning Discord answers into FAQ entries | Writing, Markdown | [Documentation structure](DocumentationStructure.md) |
| Operator descriptions | Clear first lines, parameter explanations, tips | Using TiXL, writing | [Style conventions](StyleConventions.md) |
| Examples and tutorials | Example operators, HowTos, Skill Quest levels, video tutorials | Building graphs | [Documentation structure](DocumentationStructure.md) |
| Operators | New operators built from existing ones, shader operators, C# operators | Graphs, HLSL or C# | [Creating new operators](../advanced/CreatingNewOps.md) |
| Editor and runtime | Fixes, UI features, performance, platform support | C#, .NET, graphics APIs | [Code structure](CodeStructure.md) |

If you are not sure where to start, ask in the [contribute channel](https://discord.gg/FfaTs78Ra2) on Discord. Saying what you enjoy doing is enough; we'll suggest something that fits.

## How a contribution gets merged

1. **Talk first for anything bigger than a fix.** Open an issue or ask on Discord before you start a new feature or a larger refactor. It avoids duplicate work and surprises in review.
2. **Fork and branch from `main`.** Keep one topic per branch.
3. **Keep the change small and focused.** Don't mix reformatting or unrelated clean-ups into a feature.
4. **Ship the docs with the change.** A user-visible change updates the matching page under `.help/docs/`, adds a line to the upcoming release notes and, for UI behavior, a manual test set. See [Style conventions](StyleConventions.md).
5. **Build and test before you open the pull request.** For the editor and operators, run TiXL and check the change in a real project.
6. **Open a pull request against `main`** with a short description of what changed and why. Screenshots or short clips help a lot for UI changes.

Docs-only changes are the exception to most of this: small fixes can be made directly on GitHub through the **Edit on GitHub** link at the top of every page.

## Pages in this section

- [Documentation structure](DocumentationStructure.md) — every place TiXL's knowledge lives, why, how it is built and what state it is in.
- [Code structure](CodeStructure.md) — the repository layout and where to find what.
- [Style conventions](StyleConventions.md) — writing, wiki, operator, UI and code conventions at a glance.
- [Writing style for these docs](../STYLE.md) — the full style guide for pages in `.help/docs/`.
