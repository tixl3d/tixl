# Releasing TiXL

How to publish a version for Windows, Linux and macOS. Each platform's build details are in `Windows/README.md`,
`Linux/README.md` and the `macOS/` scripts; this page is the checklist that ties them together.

## 1. Before tagging

- **Dependabot PRs.** Merge a pending "Bump the github-actions group" PR only after starting the release workflows
  once by hand on its branch (Actions → *Windows/Linux/macOS Build* → Run workflow): they don't run on PRs.
- **Release notes.** `.help/release-notes/v<major>.<minor>.md` is up to date.
- **Manual tests.** The test sets touched since the last release (`.tests-manual/`) were run, at least on Windows.

## 2. Set the version (one commit)

1. `Tixl.props`: raise `<TixlVersion>`, e.g. `4.4.0.6` → `4.4.0.7`. Everything else takes the version from here:
   the editor, the Windows installer (`installer.iss` reads it from the built `TiXL.dll`), the extension packages.
   Change `<TixlVersionSuffix>` only when moving from alpha to beta to a stable release.
2. `Linux/common/app.tixl.TiXL.metainfo.xml`: add `<release version="4.4.0.7" date="YYYY-MM-DD" />` as the first
   entry in `<releases>`. Linux app stores show this list as the version history.

Check locally before pushing:
```
python3 Installer/check-release-version.py v4.4.0.7
```

## 3. Tag and push

```
git tag v4.4.0.7
git push origin main v4.4.0.7
```

The tag must be exactly `v` plus the four-part `TixlVersion`. The editor looks for its extensions in the GitHub
release of that name, so a tag like `v4.4.0.7-beta` would leave Settings → Extensions empty. Every release workflow
starts with `check-release-version.py` and stops at once if tag, `Tixl.props` and the metainfo disagree.

## 4. What CI does

On the tag, `windows-build`, `linux-build` and `macos-build` run (about 15–20 minutes) and attach to a
**pre-release** of that tag:

| Workflow | Attaches |
|---|---|
| Windows | `Tixl-v<version>.exe`, `extensions.json` and `TiXL.<Name>.<version>.nupkg` for each optional package |
| Linux | the tarball and the Flatpak bundle |
| macOS | the signed and notarized DMG |

Each also runs `check-licences.py`, which fails if a bundled package or dependency has a licence outside the
allowlist.

## 5. After CI

- The release contains the three installers, `extensions.json` and the extension packages.
- Install one build and open Settings → Extensions: the extensions of the new version are listed.
- To make a stable release, edit it on GitHub: clear *pre-release* and set it as latest once all three platforms
  are attached.

A failed tag is not moved: fix the problem, raise the version again (step 2) and push a new tag. Delete the broken
pre-release on GitHub.
