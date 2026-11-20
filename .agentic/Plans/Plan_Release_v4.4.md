# Plan: Stable v4.4 release for Windows, Linux and macOS

**Status:** Decisions taken 2026-11-20 (`main` 9a6889092, `Tixl.props` = 4.4.0.5-alpha). Nothing implemented yet.
**Scope:** What has to happen between today's alpha and a stable 4.4 shipped on all three platforms from one
tag. Most items point into existing plans; this file holds the order and the release decisions.
**Builds on:** [Plan_WindowsSigning](Plan_WindowsSigning.md), [Plan_MacOS](Plan_MacOS.md),
[Plan_CleanInstallLayout](Plan_CleanInstallLayout.md), [Plan_ReplaceBass](Plan_ReplaceBass.md),
[Plan_SlangInProcess](Plan_SlangInProcess.md), [Plan_AutomaticTests](Plan_AutomaticTests.md).

## Where we actually are

Checked against the code on 2026-11-20. Embedding is further along than it feels:

| Dependency | Windows installer | macOS DMG | Linux tarball | Flatpak | AUR / Nix |
|---|---|---|---|---|---|
| slangc + libslang | bundled in `<app>/slang/bin` (`Installer/Windows/build-release.ps1:38-69`) | bundled (`Installer/macOS/build-dmg.sh:126-155`) | **not bundled**: `install-dependencies.sh` or the startup check downloads it into `~/.local/opt` | bundled (`flatpak/app.tixl.TiXL.yml:40`) | distro `shader-slang` + `TIXL_SLANGC_UNPINNED=1` |
| FFmpeg 7 (LGPL) | bundled, NuGet `FFmpeg.LGPL` | **missing**: dev-only fallback to Homebrew `ffmpeg@7`, which is GPL and refused by `FfmpegLibrary` | bundled, NuGet `TqkLibrary.FFmpeg.Lgpl3.Native.Linux.x64` | bundled (same) | bundled (same) |
| .NET 10 SDK | installer runs the SDK installer | user installs it (startup check says how) | user installs it (`install-dependencies.sh`) | SDK extension | distro package |
| Vulkan loader / driver | system | loader + MoltenVK in the bundle | system | runtime | system |
| Audio | `tixlaudio` (miniaudio, MIT-0) everywhere; BASS is gone | | | | |
| Signing | **unsigned** | Developer ID + notarized on `v*` tags | n/a | Flathub signs | n/a |

`SlangShaderCompiler.FindCompiler` already looks in `TIXL_SLANGC`, then `<app>/slang/bin`, then the pinned home
install, then `PATH`, so bundling on Linux needs no code change. Exported players carry a seeded shader cache,
and `Player/Program.cs:188` compiles anything missing with the same compiler.

So the two dependencies named as "the biggest issue" come down to **slang in the Linux tarball** and **an LGPL
FFmpeg for macOS**. The .NET SDK is the dependency users actually trip over, and it can't be embedded for 4.4
(see Out of scope).

## 1. Licence cleanup (do first: cheap, and it decides what "MIT on Linux" means)

The TiXL code is MIT and BASS is gone, but the release is not yet clean. `Editor/Editor.csproj:102` and
`Player/Player.csproj:36` copy **all of `Dependencies/**`** (except `linux-x64/`, `osx/`) into every build on
every platform. So the Linux tarball and the macOS DMG ship Windows-only binaries, including licences that are
not MIT:

| File in `Dependencies/` | Used by | Licence | Problem |
|---|---|---|---|
| `AbletonLink.dll`, `AbletonLinkDLL.dll`, `AbletonLink.deps.json` | `[AbletonLinkSync]` (Lib) via `DllImport("AbletonLinkDLL")` | Ableton Link is **GPLv2+** unless you hold Ableton's proprietary licence | GPL native in an MIT app on all platforms; Windows-only binary is useless elsewhere |
| `Emgu.CV.dll`, `cvextern.dll` | Mediapipe operators only | Emgu CV open-source edition is **GPLv3** (commercial licence otherwise); `licenses/GPL-v3-EmguCV.txt` is shipped | GPL in the main install on all platforms |
| `Processing.NDI.Lib.x64.dll`, `NDILibDotNet6.dll` | Ndi package | NDI SDK licence (proprietary, redistributable with conditions) | Windows binary shipped to Linux/macOS for nothing; Linux uses the system runtime |
| `DirectShowLib.dll` | nothing (no reference in any `.cs`) | LGPL-2.1 | dead weight; delete |
| `licenses/MS-Evaluation_SDK_EULA.pdf` | unknown (added in 88c24fef2) | an *evaluation* EULA | find out what it covers; drop it with whatever it belonged to |

Decided 2026-11-20: Link and Emgu may disappear from the default install. They become **separate operator
packages**, because more optional, licence-encumbered or platform-specific integrations are coming and they all
need the same home. Mediapipe, Ndi and Spout already are such packages (`Operators/<Name>/`, natives in their own
`dependencies/` folder, listed in `PackageNames` in `Editor/Editor.csproj:197`); Video was moved out of Lib the
same way. So for 4.4 this is a move, not new machinery:

1. **Ableton Link → `Operators/AbletonLink/`.** Move `AbletonLinkSync` (keep its symbol GUID) and its three
   files from `Dependencies/` into the package's `dependencies/`. Follow how Video left Lib.
2. **Emgu CV → `Operators/Mediapipe/dependencies/`.** Mediapipe is its only user.
3. **Delete** `Dependencies/DirectShowLib.dll` (unused). Identify `licenses/MS-Evaluation_SDK_EULA.pdf` and drop
   it with whatever it belonged to. Move the NDI runtime DLL into `Operators/Ndi/dependencies/` if Ndi doesn't
   already carry it.
4. **Bundling per platform.** In `PackageNames`, Spout gets a Windows-only condition (RID or
   `'$(OS)' == 'Windows_NT'`). Mediapipe leaves the list and AbletonLink never joins it: both are GPL, so neither
   is in any release build. Whatever is
   left in `Dependencies/` root must be cross-platform; Windows-only natives go to `Dependencies/win-x64/`,
   copied like `linux-x64/` and `osx/`.
5. **Getting an optional package.** 4.4: a "Packages" note in the docs plus the package's source folder, which
   the editor compiles like a user project. Later: install from the editor (a download into the packages
   folder, built on the `.nupkg` artifact from
   [share-project-and-packages-plan](share-project-and-packages-plan.md)). Check what a project that uses
   `[AbletonLinkSync]` shows when the package is missing; it must load, with a clear placeholder.
6. Write `THIRD-PARTY-NOTICES.md` per platform: every bundled component, version, licence, source link. The
   FFmpeg LGPL obligation is met by shipping shared libraries plus the exact source location and the LGPL text.
7. Then the claim holds on all three platforms: TiXL is MIT and bundles only MIT, Zlib, BSD,
   Apache-2.0 WITH LLVM-exception (Slang), MIT-0 (miniaudio) and LGPL-3.0 (FFmpeg, dynamically linked).

Verify: `tar tzf` the CI tarball and list every `.dll`/`.so`; no PE files and none of the above.

## 2. Embedding gaps

### 2a. Slang in the Linux tarball (small)

`build-tarball.sh` copies `bin/slangc` and the `lib/libslang*.so` files it needs from
`slang-<pinned>-linux-x86_64-glibc-2.27.tar.gz` into `<stage>/slang/{bin,lib}`, the same subset the Windows and
macOS scripts pick (no `slang-llvm`), and reads the version from `PinnedVersion` the same way. Check that
`SlangLibrary` finds `../lib/libslang.so` for the in-process path, and `slangc -v` from the staged folder in
CI. `install-dependencies.sh` then installs only the SDK and Vulkan, and the startup download remains a
fallback. AUR and Nix keep using the distro compiler.

### 2b. LGPL FFmpeg on macOS (the real work)

Decided 2026-11-20: video playback on macOS is required for 4.4, and the audience are artists who must never
need a terminal. That rules out Homebrew, which needs the terminal to install and ships GPL builds that
`FfmpegLibrary` refuses anyway. A startup download would be acceptable, but once we have to build FFmpeg
ourselves, putting it straight into the DMG costs less code than a downloader UI. So: **build once in CI,
bundle in the DMG.**

1. `Native/FFmpeg/build-macos.sh` + a `workflow_dispatch` workflow on `macos-14` (pattern:
   `Native/TixlAudio/build-macos.sh`, `native-audio.yml`). FFmpeg 7.1.x (avcodec-61, which `Sdcb.FFmpeg 7.0.0`
   needs), `--enable-shared --disable-gpl --disable-nonfree --disable-programs --enable-videotoolbox
   --enable-audiotoolbox`, no external libraries, `install_name` set to `@loader_path`. Expect ~15-25 MB.
2. The workflow uploads `ffmpeg-<ver>-lgpl-macos-arm64.tar.gz` to a fixed release (e.g. tag
   `deps-ffmpeg-7.1`). It's rebuilt only when the version changes, so no binaries land in git.
3. `build-dmg.sh` downloads it like it downloads Slang and copies the dylibs next to the Video package. They're
   signed and notarized with the rest of the bundle.
4. `FfmpegLibrary` on macOS: look in the app first, keep Homebrew only as the developer fallback. Developers can
   also unpack the same archive and point `TIXL_FFMPEG_DIR` at it, so they test the shipped build.
5. Encoding without libx264 (GPL): check that `FfmpegVideoExport` can pick `h264_videotoolbox` /
   `hevc_videotoolbox` on macOS. Playback is the must-have; export may follow in a 4.4.x.

Verify on a Mac without Homebrew: `PlayVideo` plays, `FfmpegLibrary` logs "LGPL", and `otool -L` on the dylibs
shows no `/opt/homebrew`.

## 3. Windows signing

Decided 2026-11-20: Framefield qualifies; budget 100-200 per year; at most a couple of hours of the
maintainer's time. Azure Artifact Signing Basic (5,000 signatures/month) is ~10 USD/month (~120 per year) per
[Plan_WindowsSigning](Plan_WindowsSigning.md). The pricing page hides the number until you sign in, so
confirm it in the calculator. Billing starts when the signing account is created.

**Maintainer's part (one sitting, plus waiting for Microsoft):**
1. Azure subscription for Framefield GmbH (or reuse one).
2. Create an Artifact Signing account (Basic) and submit the **public trust identity validation** with the
   company documents. Then wait; Microsoft may ask follow-up questions by mail.
3. After approval, create a certificate profile.
4. Create an app registration for GitHub OIDC with the "Artifact Signing Certificate Profile Signer" role, and
   put its IDs into a `windows-signing` GitHub environment (only `v*` tags, like `macos-signing`).

**Everything else is agent work:** signing steps in `windows-build.yml` and `build-release.ps1` (TiXL exes and
DLLs first, then the Inno Setup installer), plus a `signtool verify` check in CI.

**Order:** sign the Windows pre-releases as soon as the identity is approved, so SmartScreen reputation builds
up before Windows gets its stable 4.4. The clean install layout waits on Windows until signing has settled,
because it changes which exe gets signed.

## 4. Release mechanics

- **One tag, three platforms.** `windows-build.yml`, `linux-build.yml` and `macos-build.yml` already build on
  `v*` tags and attach to the release as pre-releases. For stable, add a final step (or a manual edit) that
  flips the release to non-prerelease and "Latest" only after all three uploaded.
- **Version:** `Tixl.props` `TixlVersionSuffix` alpha → beta → empty. `installer.iss` MyAppVersion and the
  metainfo `<release>` entry are updated at tagging time (decided 2026-10-04).
- **Tests in CI.** No workflow runs `dotnet test` yet. Before beta, at least `Core.Tests` and
  `VideoServices.Tests` on all three runners; the Slang tests skip themselves where no compiler/GPU exists.
- **Manual test round** per platform from `.tests-manual/`, on fresh machines or VMs without a dev setup
  (no SDK preinstalled on Linux/macOS; that's exactly the first-run path users hit).
- **Update check / release notes:** whatever announces new versions in the editor must not offer a Linux or
  macOS pre-release to Windows users. Check before the first beta tag.

## Milestones

| | What | Blocks |
|---|---|---|
| M0 | Start Azure validation (§3, maintainer). AbletonLink package, Emgu into Mediapipe, `Dependencies/` split (§1) | licence-clean builds |
| M1 | Slang in the tarball (§2a), macOS FFmpeg build + bundling (§2b), clean install layout on Linux, tests in CI | beta |
| M2 | `v4.4.0-beta.1` for macOS and Linux, tester round. Windows: signed pre-releases as soon as Azure approves | stable |
| M3 | Fixes from testers, `v4.4.0` for macOS and Linux, Flathub submission. Windows stable 4.4 once signing has run a few weeks | |

## Decisions (2026-11-20)

1. **Link and Emgu:** fine to lose from the default install; separate optional packages (AbletonLink new,
   Emgu into Mediapipe). Installing optional packages from the editor comes later and must work for more
   packages to come.
2. **macOS video:** required for 4.4, no terminal for users. LGPL FFmpeg built in CI, bundled in the DMG.
3. **Rollout:** 4.4 stable ships first for macOS and Linux (with the clean install layout on Linux). Windows
   gets signing first and its stable 4.4 later.
4. **Signing:** Azure Artifact Signing Basic; Framefield qualifies; ~120 per year; maintainer time limited to
   the account and validation steps in §3.

Assumed, not confirmed: decision 3 also means Windows keeps the flat install layout for now.

## Out of scope for 4.4

- **Embedding the .NET SDK.** Operators compile at runtime with `dotnet restore`/`build`, which needs the full
  SDK and NuGet access. A private SDK inside the app is ~200 MB and still needs network for restore. The
  installers and startup check already handle it; revisit with an offline package feed later.
- Intel Macs, ARM Linux, Mediapipe and Emgu on Linux/macOS, installing optional packages from inside the editor.
