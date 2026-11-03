# Documentation structure

TiXL's knowledge lives in more places than this website: in the editor's tooltips, in example operators, in interactive tutorials, on YouTube and on the GitHub wiki. This page lists each of them: what it is for, where its source lives, how it is built and what state it is in. Read it before you add documentation, so the new content ends up where people will look for it.

## Why there are so many surfaces

People learn differently, and they look for help at different moments. A tooltip has to answer in one line while you are dragging a connection; a manual page can take ten minutes to explain a concept. TiXL therefore offers help at several depths, and each surface has one job:

| Depth | Surface | Answers |
| --- | --- | --- |
| Glance | Tooltips, the first line of an operator description | "What is this?" |
| Context | Operator and parameter descriptions, UI topic help | "How does this work?" |
| Guided | HowTo operators, examples, tours | "Show me once." |
| Practice | Skill Quest | "Let me try it." |
| Reference | This manual, the operator reference | "Explain everything." |
| Archive | Video tutorials and meet-up recordings | "Show me someone doing it." |

Two rules keep this manageable:

- **Write each fact once, in the surface that fits it best,** and link to it from the others. When the same explanation exists in three places, two of them will be wrong in a year.
- **Links point one level deeper.** A tooltip links to the description, a description to the manual or a video. The reverse links ("discussed in this video", "used in this example") are generated from indices, never written by hand.

## The manual

The pages you are reading now.

- **Source:** [`.help/docs/`](https://github.com/tixl3d/tixl/tree/main/.help/docs) in the main repository, one Markdown file per page, grouped into `getting-started/`, `install/`, `using/`, `advanced/` and `contributing/`.
- **Built with:** [MkDocs](https://www.mkdocs.org/) and the Material theme, configured in `.help/mkdocs.yml`. The build runs in strict mode, so a broken internal link fails it. The `awesome-pages` plugin reads the `.pages` file in each folder for the navigation order.
- **Published:** to [help.tixl.app](https://help.tixl.app/) by Vercel, built from the `main` branch.
- **Preview locally:**

  ```bash
  pip install -r .help/requirements-docs.txt
  mkdocs serve -f .help/mkdocs.yml
  ```

  Then open `http://127.0.0.1:8000/`. The page reloads when you save a file.
- **Maintained by:** whoever changes a feature updates its page in the same pull request. Each section `README.md` lists the pages that exist and those that are still to write.
- **Current state:** about 55 pages, migrated from the wiki in 2026. The **Using** section has the largest gaps: the Graph, Parameter and Output windows, essential operators and recipes have no pages yet.

## Operator reference

One page per library operator, under [Operators](../operators/index.md).

- **Source:** the operator's **Description** and parameter descriptions, stored in its `.t3ui` file next to the operator in `Operators/Lib/Symbols/`. They are the same texts the editor shows in the parameter window and in tooltips.
- **Built with:** the editor. **Help → Development Tools → Documentation → Export as WIKI** writes the Markdown pages and `operators/index.json` into `.help/docs/operators/`.
- **Linked automatically:** an operator name in square brackets, such as `[Blob]`, in any manual page becomes a link to its reference page. The hook `.help/scripts/docs/op_autolinks.py` does this during the build.
- **Maintained by:** editing descriptions in the editor (or in the `.t3ui` while the editor is closed), then re-exporting. **Never edit the generated pages**; they are overwritten.
- **Current state:** around 890 pages for about 970 library operators. Many descriptions are still a single line, so improving them is one of the most effective contributions.

## Embedded help and UI topics

Short help texts that the editor shows in its own windows: the documentation icon in panels, the help popovers in project settings, the hover help for UI elements.

- **Embedded help:** [`.help/embedded/`](https://github.com/tixl3d/tixl/tree/main/.help/embedded), one Markdown file per id (for example `ProjectSettings_Audio.md`). The editor loads them at runtime; release builds ship a copy next to the binaries.
- **UI topics:** [`.help/references/topics/`](https://github.com/tixl3d/tixl/tree/main/.help/references/topics). `ui-topics.md` registers each UI component or concept with a stable id, synonyms and the classes that implement it. The help text of each topic lives in `topics/ui/<Id>.md`. A script compiles them into `references/indices/topics.json`, which the editor reads.
- **Format:** a subset of Markdown that the editor can render: headings, bold, inline code, lists and `[Operator]` or `[ui:Topic]` links. No images or tables.
- **Current state:** about 50 UI topics are registered; many of their help texts are still drafts distilled from meet-up transcripts.

## HowTos and examples

Help that is itself a TiXL graph. You open it in the editor, look at it and change it.

- **HowTo operators** in `Operators/Examples/Symbols/howto/` are guided introductions to areas such as animation, colors, points and particles. They predate Skill Quest and are less structured, but are still useful entry points.
- **Example operators** in `Operators/Examples/Symbols/Lib/` mirror the library's namespaces. An operator named `CameraExample` demonstrates [Camera], and the editor offers it from the operator's help panel.
- **Templates** in `Operators/Examples/Symbols/templates/` are starting points for new projects and operators.
- **Maintained by:** building them in the editor from a development checkout and committing the `.t3`, `.t3ui` and `.cs` files.
- **Current state:** about 130 examples for roughly 970 library operators. A missing example is a good first contribution: build a small, clean setup that shows what the operator is for.

## Skill Quest

Interactive tutorials in a game-like mode, started from the project hub. See [Skill Quest](../getting-started/SkillQuest.md) for the design.

- **Source:** the levels are operators in `Operators/Skills/Symbols/`, grouped into topics (`Basics`, `Image`, `Math`, `Mesh`, `Points`, `Render`). Each level is a puzzle plus a short tour. Planning notes and the topic overview live in the separate [skillquest](https://github.com/tixl3d/skillquest) repository.
- **Maintained by:** building levels in the editor and adding them to the skill map.
- **Current state:** a first set of topics ships with TiXL. More levels, especially for 3D, particles and audio, are welcome.

## Video tutorials and meet-ups

- **Tutorials:** curated YouTube playlists, listed on [Video tutorials](../getting-started/VideoTutorials.md) and in the editor's **Help** menu.
- **Meet-ups:** the community meet-up every second Monday is recorded and uploaded. Each one gets a notes page on the wiki (`meetup.YYYYMMDD`) with deep-linked chapters.
- **Indexed:** recordings are transcribed, and every chapter where an operator or UI element is discussed is recorded in `.help/references/indices/`. The editor uses this index to show "discussed in this video" links next to operators. The pipeline is described in [`DOCUMENTATION_ECOSYSTEM.md`](https://github.com/tixl3d/tixl/blob/main/.agentic/DOCUMENTATION_ECOSYSTEM.md).
- **Current state:** around 60 meet-ups are analysed and indexed. Some older meet-up pages on the wiki still lack titles.

## The wiki

The [GitHub wiki](https://github.com/tixl3d/tixl/wiki) is its own git repository (`tixl3d/tixl.wiki`).

- **What belongs there:** developer and contributor topics (`dev.*` pages such as coding conventions, operator conventions, working with git, integration tests, RenderDoc) and the meet-up notes (`meetup.*`).
- **What does not:** anything a TiXL user needs. User documentation moved to this manual in 2026. The old `help.*` pages on the wiki only remain as redirects, each with a banner pointing to its new location, maintained by `.help/scripts/wiki/update_banners.py`.
- **Conventions:** see [Style conventions](StyleConventions.md#wiki).
- **Current state:** the sidebar is up to date; the wiki **Home** page still lists the old `help.*` pages and links to a conventions page that no longer exists. Some `dev.*` pages, such as `dev.Contributing`, date from Tooll3.

## Release notes

- **Source:** [`.help/release-notes/`](https://github.com/tixl3d/tixl/tree/main/.help/release-notes), one file per minor version (`v4.4.md`).
- **Shown:** in the editor's welcome window after an update.
- **Maintained by:** every pull request with a user-visible change adds one short bullet to the upcoming version's file.

## Manual test sets

- **Source:** [`.tests-manual/`](https://github.com/tixl3d/tixl/tree/main/.tests-manual), about 110 step-by-step walkthroughs of features, one file per feature.
- **Used:** by people testing a release, either by reading the files or through the **Guided Feature Tests** window in the editor.
- **Maintained by:** pull requests that change UI behavior add or update the matching test set. The format is described in the folder's `README.md`.

## Notes for coding agents

[`.agentic/`](https://github.com/tixl3d/tixl/tree/main/.agentic) holds instructions for AI coding assistants: coding and performance rules (`AGENT_INSTRUCTIONS.md`), an architecture map (`SOLUTION_OVERVIEW.md`), the debug protocol and implementation plans. They are written for agents, but they are also the most current written summary of the code conventions, so they are worth reading for human contributors too.

## Where to put new knowledge

| You have… | Put it in… |
| --- | --- |
| A one-line answer about an operator | The operator's description |
| An explanation of a parameter | The parameter's description in the `.t3ui` |
| A Discord answer others will need | An FAQ entry in the manual |
| A workflow or concept explanation | A manual page in `using/` or `advanced/` |
| A setup that shows how an operator is used | An example operator |
| A step-by-step exercise | A Skill Quest level |
| Help for an editor panel or control | Embedded help or a UI topic |
| Notes on building, debugging or releasing TiXL | A `dev.*` page on the wiki |
| A user-visible change you just made | The release notes and a manual test set |

If a topic has no obvious home, add a line to the "Still to write" list of the closest section `README.md`, or open an issue.

## See also

- [Style conventions](StyleConventions.md)
- [Code structure](CodeStructure.md)
