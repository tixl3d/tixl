# Contributing

TiXL is an open, volunteer-run project. There are several ways to help, and most of them don't require touching C#.

## Help with these docs

Every page on `tixl.app/help/` is sourced from the [`.help/`](https://github.com/tixl3d/tixl/tree/main/.help) directory of the main repo. Each rendered page has an **"Edit on GitHub"** link at the top-right that opens the source file.

Before opening a PR, please read [`STYLE.md`](https://github.com/tixl3d/tixl/blob/main/.help/STYLE.md). Short version: short pages, plain language, relative links, operator names in `[brackets]`.

Good first contributions:

- Fix a typo or unclear sentence you stumbled on.
- Fill in a section listed as "Still to write" on any section README.
- Turn a meet-up explanation or Discord answer into a one-paragraph FAQ entry.

## Report a bug or suggest a feature

See [Reporting bugs and feature requests](../getting-started/ReportBugs.md).

## Contribute to TiXL itself

Developer-facing docs (how to build from source, the coding conventions, how releases work, how CI is set up) live on the **[GitHub wiki](https://github.com/tixl3d/tixl/wiki)** under the "Developer docs" section of the sidebar. They're kept editable there because contributors iterate on them directly.

If you want to start developing, the entry points are:

- [Set up a development environment](../install/InstallDev.md) — in these docs, because it's the same instructions end users need for writing C# operators.
- `dev.UsingDev` on the wiki — running TiXL from your IDE.
- `dev.CodingConventions` on the wiki — style and structure.
- `dev.Contributing` on the wiki — how PRs get reviewed and merged.
