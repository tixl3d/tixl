# Packaging TiXL for Linux

Notes for distro packagers (AUR, Nix, Flatpak, ...). The Linux port is a preview: the Editor runs with the
Vulkan backend, but expect gaps.

## Dependencies

`install-dependencies.sh` is the stopgap for testers until packages exist — see
[DEPENDENCIES.md](DEPENDENCIES.md) for the table it is derived from.

## Build

```bash
Installer/Linux/build-tarball.sh               # build + stage + Output/tixl-<version>-linux-x64.tar.gz
Installer/Linux/build-tarball.sh --stage-only  # build + stage only (for distro packages)
Installer/Linux/build-tarball.sh --skip-build  # restage an existing Release build
Installer/Linux/build-tarball.sh --system-dotnet  # editor uses the system .NET instead of bundling it
```

It publishes the Player (self-contained) and builds `t3.sln` in Release, like the Windows
`Installer/Windows/build-release.ps1`. The staged folder `Output/tixl-<version>-linux-x64/` is the
single source of truth for the layout. Distro packages should copy it, not re-derive it.

- `TiXL`: the Editor. It bundles the .NET runtime, so it always starts and can report missing dependencies
  in a dialog. Distro packages that depend on the SDK anyway can pass `--system-dotnet` to skip the bundle.
- `Player/`: self-contained Player, copied into exports.
- `Operators/`: precompiled read-only operator packages (Lib, Examples, ...).
- `share/`: `.desktop`, AppStream metainfo and icon, laid out like `/usr/share`.

The app id is `app.tixl.TiXL` (reverse DNS of tixl.app).

## Runtime requirements

The full list with package names per distro is in [`DEPENDENCIES.md`](DEPENDENCIES.md). In short:

- **The .NET 10 SDK, not just the runtime.** TiXL runs `dotnet restore` and `dotnet build` on user
  operator projects while it runs, calling `dotnet` from `PATH`. Restore needs network access to NuGet
  and writes to `~/.nuget`.
- Vulkan loader and driver.
- `slangc` (Slang 2026.18) for shader compilation, found on `PATH` or through `TIXL_SLANGC`.
- BASS for audio (`libbass.so`, `libbassmix.so`, `libbassflac.so`). These are committed under
  `Dependencies/linux-x64/`; see its README for un4seen's terms. `fetch-bass.sh` restores them if they go
  missing and refreshes them with `--force`.

## Where TiXL writes

The install folder is treated as read-only (the Windows installer puts it in Program Files):

- Settings, logs, temp files, shadow copies: `~/.config/TiXL<version>/`
  (.NET's `ApplicationData`, honours `XDG_CONFIG_HOME`).
- User projects: `~/Documents/TiXL<version>/` (.NET's `MyDocuments`, from `XDG_DOCUMENTS_DIR`).

`<version>` is e.g. `4.3-alpha`; `TIXL_OVERRIDE_VERSION_ID` replaces the suffix to keep installs apart.

## Licenses

TiXL is MIT. Bundled third-party licenses are in `Dependencies/licenses/`. Two need attention:

- **BASS**: free for non-commercial use only (`LicenseRef-BASS` on Arch, `unfree` in nixpkgs).
- **NDI**: proprietary runtime, used by the Ndi operator package.

## CI

`.github/workflows/linux-build.yml` builds the tarball in an `ubuntu:22.04` container. The bundled `libcimgui.so` from ImGui.NET still
requires glibc 2.38, so that is the effective minimum. A version tag (`v*`) attaches the tarball to that tag's
release; manual runs upload it as a workflow artifact.

## Formats

- `aur/PKGBUILD`: reference `tixl-git` package. It downloads BASS and installs to `/usr/lib/tixl`
  with a `/usr/bin/tixl` symlink.
- Flatpak and Nix: not started. Nix needs the SDK in the wrapper's `PATH`/`DOTNET_ROOT`, and its
  offline builds need NuGet dependencies pinned (`fetch-deps`).
