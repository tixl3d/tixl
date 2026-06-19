# Plan: Install Verification & Safe Startup

## Motivation

A class of Sentry crashes report "couldn't load assembly X.dll" or "couldn't find file Y" where X/Y is a file that TiXL ships with its install. The cause is rarely a TiXL code bug — it's usually one of:

- Antivirus quarantined the file (Bitdefender, Norton, Windows Defender heuristics flagging native DLLs like `swiftcam.dll`, `ManagedBass.Wasapi.dll`).
- Disk full or interrupted install — InnoSetup completed but some files were truncated or skipped.
- File system corruption (bad sectors, OneDrive virtualisation glitches on the install path).
- Manual user action — deleted a file thinking it was unused, moved the install folder, etc.

The naive response is per-file: each time a new "missing X" Sentry report comes in, wrap the call site in try/catch and log "X disabled — reinstall." That is a **whack-a-mole game** — it expands the codebase indefinitely without solving the underlying problem: there's no single place where TiXL can tell the user "your install is incomplete; reinstall." Every code path that touches a deployed file is a potential per-file fix, and a *new* freak event affecting a *new* file produces the same pattern again.

The shape this plan addresses: detect install corruption **once, centrally**, with a recovery path that doesn't require code changes per file. Pairs naturally with [Plan_RuntimeConsistencyRecovery](Plan_RuntimeConsistencyRecovery.md) (which handles runtime invariant violations) and [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md) (which handles user-project load failures) — same recovery primitives (snapshot, suspend-and-recover dialog, restart), different detection surfaces.

## Trip-source catalogue (motivating examples)

| Sentry | File class | Notes |
|---|---|---|
| [TOOLL3-XF](https://tooll.sentry.io/issues/7327839818/) | `ManagedBass.Wasapi.dll` | Assembly JIT failure on every render frame. User has 10+ projects, so a power user with a real install. Single user / 3 hits. |
| [TOOLL3-Y4](https://tooll.sentry.io/issues/) | `Silk.NET.Input.Common.dll` | Same shape — assembly missing at runtime. |
| [TOOLL3-Y5](https://tooll.sentry.io/issues/) | `Silk.NET` platform-not-supported | Silk's windowing layer expected a platform DLL it couldn't resolve. Same root class. |
| [TOOLL3-Y1](https://tooll.sentry.io/issues/) | `EditorResources/shaders/fullscreen-texture.hlsl` | Resource file missing — *not* an assembly, but same install-integrity story. The verifier should treat resource files and DLLs uniformly. |

Once Phase 2 (the verifier) lands, all four of these resolve as "auto-resolved by install verifier" — and any future Sentry hit of the same shape resolves automatically too without a code change.

---

## Design

### Two complementary mechanisms

1. **Reactive — `AssemblyResolve` diagnostic logger (Phase 1).** A handler registered on `AppDomain.CurrentDomain.AssemblyResolve` (and `AssemblyLoadContext.Default.Resolving`) that fires *only* when the default loader can't find an assembly. Zero cost on the happy path. Doesn't prevent the crash (returning `null` lets the CLR proceed to throw `FileNotFoundException`), but it produces a clear log line *before* the exception is thrown, which lands in the Sentry breadcrumbs of the eventual crash report. Instead of an opaque "couldn't load X.dll" with no context, future Sentry events for this class carry: *which* DLL, *which assembly requested it* (`args.RequestingAssembly`), and the stack at resolution time. Lets us actually diagnose recurrences.

2. **Proactive — startup install verifier (Phase 2).** At app startup, walk a manifest of expected deployed files and verify each is present and readable. On failure, surface a recovery dialog: "TiXL detected missing files in its install. Reinstall recommended. Files affected: X.dll, Y.hlsl." Three trigger conditions:
   - **First launch** (every time, fast — file existence + size only).
   - **After a crash** (the `StartUp` crash-lock pattern already exists in [`StartUp.cs`](Editor/Gui/Interaction/StartupCheck/StartUp.cs) for backup recovery — extend it to also run install verification before offering project restoration).
   - **User-initiated "Safe Startup"** (a separate Start Menu entry / `--safe` CLI flag — see Phase 3). Full checksum verification, not just existence.

The two are **independent and complementary**:
- The diagnostic logger catches DLL load failures that slip past the verifier (e.g. a transitive dependency the manifest didn't know about, or a DLL that was fine at startup but got quarantined mid-session).
- The verifier prevents the most common case — corruption detected before the user sees anything bad happen.

### AssemblyResolve diagnostic logger — details

```csharp
// Pseudo-code, see Phase 1 implementation for exact shape
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    var requestingAssembly = args.RequestingAssembly?.GetName().Name ?? "<unknown>";
    Log.Warning($"Failed to resolve assembly '{args.Name}'. " +
                $"Requested by '{requestingAssembly}'. " +
                $"This usually indicates a missing or corrupt TiXL install file. " +
                $"Reinstalling TiXL is recommended.");
    return null; // let the CLR throw FileNotFoundException as normal
};
```

Register both in `Editor/Program.cs:Main` and `Player/Program.cs:Main`, as early as possible (before any other code that might trigger an assembly load). A shared helper `Core/Diagnostics/AssemblyLoadDiagnostics.cs` keeps the logic in one place.

`AssemblyLoadContext.Default.Resolving` is the modern equivalent for `.NET 5+`; the legacy `AppDomain.AssemblyResolve` still works and is broader. Register both for full coverage.

### Startup install verifier — details

Manifest format — a generated JSON or text file shipped alongside the executable, listing every expected deployed file with size (and optionally SHA256):

```json
{
  "manifestVersion": 1,
  "tixlVersion": "4.3.0",
  "files": [
    { "path": "Core.dll", "size": 2348112 },
    { "path": "ManagedBass.Wasapi.dll", "size": 89320 },
    { "path": "EditorResources/shaders/fullscreen-texture.hlsl", "size": 2103 },
    ...
  ]
}
```

Generated by the build/publish pipeline post-publish, before InnoSetup packaging.

**Fast verification (always, ~milliseconds):**
- Existence + size check on every entry. A file that's the wrong size means it's been truncated, partially overwritten, or modified.

**Full verification (Safe Startup only, several seconds):**
- SHA256 over every file. Catches corruption that preserves the byte count.

**On failure:**
- Show a recovery dialog (same modal infrastructure as [Plan_RuntimeConsistencyRecovery](Plan_RuntimeConsistencyRecovery.md)'s dialog — *not* `BlockingWindow.ShowMessageBox`, which itself depends on DLLs that may be missing).
- Body: list of affected files, what they probably do ("audio capture", "fullscreen rendering"), what the user should try ("reinstall from https://tixl.app").
- Actions: **Reinstall** (opens the download page in browser), **Continue Anyway** (proceeds at user's risk, banner persists), **Restart** (in case the user manually restored the file).
- The dialog must be drawable using only assemblies that are always available — `Core`, `ImGui.NET`, `SilkWindows` — so a missing user-area DLL doesn't take down the dialog itself. (Bootstrap risk: see "Notes / risks" below.)

### Safe Startup mode — details

A separate entry point (`--safe` CLI flag, plus a Start Menu shortcut entry alongside the normal one):

- Always runs full SHA256 verification, not just size.
- Always shows the "what was checked" report, even if everything passes — gives the user a positive signal that their install is healthy.
- Does **not** auto-load the user's last project or restore backups; the user gets a fresh "no project open" state. Forces deliberate next-step.
- Pairs with [Plan_BrokenPackageRecovery Phase 2](Plan_BrokenPackageRecovery.md#phase-2-in-editor-backup-browser--restore) — the backup browser is what the user uses *from* Safe Startup to restore a clean project state.

---

## Phases

### Phase 1 — AssemblyResolve diagnostic logger

**Goal:** zero per-file code changes; every future missing-DLL Sentry report carries useful diagnostic context.

**Tasks:**
1. New file `Core/Diagnostics/AssemblyLoadDiagnostics.cs` with `Install()` static method that registers the handler.
2. Call from `Editor/Program.cs:Main` and `Player/Program.cs:Main` as the first line of `Main`.
3. The handler logs via `Log.Warning` so Sentry breadcrumbs pick it up automatically (`Sentry.SerilogIntegration` / built-in Sentry breadcrumbs already capture Log.* output — confirmed by the breadcrumbs section in TOOLL3-XF's event payload).

Independent of the rest of the plan. Lands in its own commit.

### Phase 2 — Startup install verifier

**Goal:** detect install corruption proactively; resolve TOOLL3-XF / Y4 / Y5 / Y1 (and any future siblings) via the verifier rather than per-file try/catch.

**Tasks:**
1. **Manifest generation.** Add a post-publish step (MSBuild target or PowerShell script) that walks `Editor/bin/Release/net*-windows/` after publish, generates `install-manifest.json` with path + size for every file. Optionally a SHA256 column behind a build flag.
2. **Verifier**: `Core/Diagnostics/InstallVerifier.cs`. Methods: `bool TryFastVerify(out List<string> missingOrAltered)`, `bool TryFullVerify(out List<string>)`.
3. **Bootstrap-safe recovery dialog.** Cannot reuse `BlockingWindow.ShowMessageBox` (depends on SilkWindows + its DLLs). Either:
   - Use a raw Win32 `MessageBox` via P/Invoke (zero managed dependencies beyond `user32.dll`), accepting plain text only.
   - Or detect *which* files are missing first; if `SilkWindows.dll` and its deps are intact, use the normal ImGui modal; only fall back to Win32 if the message box infrastructure itself is broken.
4. **Crash-lock integration.** Extend the existing `StartUp` flow ([`StartUp.cs`](Editor/Gui/Interaction/StartupCheck/StartUp.cs)) — if the previous session left a lock, run the verifier *before* offering project restoration.
5. **Manifest delivery.** InnoSetup script needs updating to include `install-manifest.json` in the deployed file set (and to *not* include itself in the manifest — chicken-and-egg).

### Phase 3 — Safe Startup mode

**Goal:** an explicit clean-state entry point for users with corrupted installs or unknown crashes.

**Tasks:**
1. CLI flag `--safe` in `Program.Main`. Bypasses project-restore and forces full verification.
2. New Start Menu shortcut added by InnoSetup — `TiXL (Safe Mode).lnk` pointing at the same exe with `--safe`.
3. Result UI: a Safe-Mode banner stays visible until the user closes it; explains what's been checked, what wasn't, and offers the "Open Project…" and "Restore From Backup…" buttons explicitly.
4. Hook into [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md) Phase 2 — the backup browser is offered directly from Safe Mode's UI.

---

## Notes / risks

- **Bootstrap chicken-and-egg.** The verifier and its recovery dialog must work even when *parts* of the install are missing. The verifier itself depends on `Core.dll`; if that's missing, nothing we ship can run. Beyond `Core.dll`, every other DLL should be optional from the verifier's POV — its dialog must be raw Win32 if anything ImGui-related is suspect. **Constraint**: write the verifier as pure managed code in `Core` with no external NuGet refs that aren't already in `Core`.
- **Antivirus false positives.** Antivirus is a leading cause of these reports — but it's also unpredictable. A user who reinstalls may have antivirus quarantine the same file again on first run. The recovery dialog should hint at this: "If reinstalling repeatedly produces the same result, your antivirus may be quarantining TiXL files."
- **Manifest staleness.** If the build pipeline regenerates the manifest at publish time, manual rebuilds during development would have a stale manifest. Solution: skip verification in `Debug` builds (it's only meaningful for installed/published copies).
- **Manifest as attack surface.** A user who edits the manifest to remove an entry can suppress verification for that file. Not a real attack vector (the user can also delete the file directly), but worth a one-line note: the manifest is advisory, not security-critical.
- **`AssemblyResolve` performance.** The handler only fires when default resolution *fails* — which is the slow path anyway. Registering it has no measurable cost on the happy path. Confirmed by .NET documentation and standard practice.
- **Sentry double-reporting.** Phase 1's handler logs a Warning, which becomes a Sentry breadcrumb. Then the eventual `FileNotFoundException` is captured by Sentry's unhandled-exception integration. We get one event per crash, with the diagnostic Warning in its breadcrumb chain — correct shape, no duplication.

---

## Manual test set

Add `.tests-manual/install-verification.md` covering:

1. **Healthy install** — first launch, no Safe Mode, no banner.
2. **Missing DLL, fast verifier** — close TiXL, rename `Core/ManagedBass.Wasapi.dll`, relaunch. Expect the verifier dialog naming the missing file; restoration brings the editor back.
3. **Truncated DLL** — close TiXL, truncate a known DLL to zero bytes, relaunch. Verifier detects size mismatch, same dialog.
4. **Missing resource file** — same as #2 but with `EditorResources/shaders/fullscreen-texture.hlsl`. Verifier treats resources the same as DLLs.
5. **Safe Mode entry** — launch with `--safe`. Expect SHA256 verification (slow), positive "everything healthy" report when nothing's wrong, no project auto-load.
6. **AssemblyResolve diagnostic logger** — close TiXL, rename a transitive DLL that the verifier *doesn't* know about (simulating a manifest gap). Relaunch; expect the editor to still die with `FileNotFoundException`, but the log / Sentry breadcrumb to clearly identify which DLL and which requesting assembly.
7. **Antivirus simulation** — close TiXL, quarantine a DLL via Windows Defender → "Move to quarantine." Relaunch. Verifier detects missing file. Dialog text includes the antivirus hint.

---

## Out of scope

- **Auto-repair**: actually downloading replacement files from a CDN. We just guide the user to reinstall. Auto-repair would need code-signed update infrastructure that's a much bigger commitment.
- **Per-user-project install verification**: this plan is about TiXL's *own* deployed files. User-project files are [Plan_BrokenPackageRecovery](Plan_BrokenPackageRecovery.md)'s domain.
- **Optional user-supplied components**: the GPL `ffmpeg.exe` for software H.264/HEVC export ([Plan_FfmpegEncode](Plan_FfmpegEncode.md)) is **deliberately not shipped** and lives in AppData, not the install — it must **not** be added to the manifest (it would always report "missing"). The *bundled LGPL decode* DLLs next to `Lib.dll` are shipped and do belong in the manifest.
- **Cross-platform**: the verifier is Windows-only because TiXL is currently Windows-only. If/when there's a Linux/macOS build, the manifest format generalises trivially; the Win32-fallback recovery dialog does not.
- **Detecting which file *should* contain `X` when it's been replaced by something else of the same size.** SHA256 verification in Safe Mode covers this; fast verification deliberately doesn't.

---

## Order of work

1. **Phase 1** — AssemblyResolve logger. Smallest, independent, can ship today. Resolves none of the catalogued Sentry items by itself, but enriches all future reports.
2. **Phase 2** — Install verifier + first-launch + crash-lock integration. Resolves the catalogued items (TOOLL3-XF / Y4 / Y5 / Y1) as "auto-detected at startup."
3. **Phase 3** — Safe Mode entry point + integration with backup browser. Polish on top of Phase 2; no new failure modes solved, but a cleaner UX for "I don't know what's wrong, take me to a known-good state."

## Decisions still to make

- **Manifest format**: JSON (human-readable, slightly larger) vs. binary (compact, opaque). Lean JSON for inspectability during development.
- **SHA256 in fast verification**: keep it Safe-Mode-only as proposed, or always check a small whitelist of "critical" DLLs (the ones whose corruption causes silent misbehaviour rather than load failure)? Lean Safe-Mode-only for now; revisit if a critical DLL ends up on a future Sentry list.
- **Where the manifest lives at install time**: alongside the exe (`install-manifest.json`) vs. in a subfolder (`./Resources/install-manifest.json`). Alongside the exe is simpler but clutters the install root by one file. Subfolder it is.
- **Dialog technology for the worst-case bootstrap**: Win32 `MessageBox` via P/Invoke (works under any condition) vs. a tiny ImGui-only fallback shipped with `Core.dll`. Lean Win32 — it's what survives the most corruption scenarios.
