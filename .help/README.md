# `.help/` — source for the TiXL docs site

This directory is the source of truth for TiXL's user-facing documentation. It ships with the code and publishes to `tixl.app/help/`.

- **Public landing page:** [`index.md`](index.md)
- **Writing style and conventions:** [`STYLE.md`](STYLE.md) — read before opening a docs PR.
- **Plan and open work:** [`../.agentic/Plans/Plan_UpdateHelp.md`](../.agentic/Plans/Plan_UpdateHelp.md)
- **Source material (scripts, release notes, etc.):** [`.src/`](.src/) — raw tutorial scripts and transcripts used as draft material. Not published.

Developer-focused pages (building TiXL from source, coding conventions, CI, release process) stay on the [GitHub wiki](https://github.com/tixl3d/tixl/wiki). Don't mix the two — user-facing here, contributor-facing there.

## Layout

```
.help/
├── index.md              # site landing page
├── README.md             # this file (contributor orientation)
├── STYLE.md              # writing conventions
├── getting-started/      # what is TiXL, install pointers, concepts, tutorials
├── install/              # installation and dev environment
├── using/                # day-to-day reference — UI, graphs, IO, export, live
├── advanced/             # custom shaders, C# ops, fonts
├── contributing/         # docs + dev onramps
├── operators/            # auto-generated from SymbolUi (do not hand-edit)
└── .src/                 # raw source material for future pages (not published)
```

Each section folder has its own `README.md` that lists the pages present *and* the pages still to write. When a topic is missing, don't create an empty placeholder — add a one-liner under "Still to write" and flesh it out when you have something concrete to say.

## Section READMEs

- [Getting started](getting-started/README.md)
- [Install](install/README.md)
- [Using TiXL](using/README.md)
- [Advanced](advanced/README.md)
- [Contributing](contributing/README.md)
