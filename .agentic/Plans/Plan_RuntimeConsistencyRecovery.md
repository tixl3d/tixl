# Plan: Runtime Consistency Recovery

## Motivation

The editor today has no unified response to "internal state is corrupt." Different failure surfaces are handled in ad-hoc ways:

- **Defensive null-guards** sprinkled at accessor boundaries (e.g. [Texture.cs:136](Core/DataTypes/Texture.cs:136) returns `Resource?` via `?.`, while line 135 NREs on null) — they prevent the crash but mask the underlying corruption, so the editor keeps running on stale or partial data.
- **`try/catch` swallows** that log a warning and move on (`SrvManager.CreateNewSrv`, `WindowsUiContentDrawer.DisposeDeviceObjects`, several operator update paths) — same masking concern.
- **Unhandled exceptions** that escape to Sentry and terminate the process (`AppDomain.UnhandledException`) — preserves diagnostic clarity but loses any unsaved work and gives the user no recovery path.
- **No response at all** for resource-exhaustion classes the user has no way to anticipate: `OutOfMemoryException`, `IOException` mid-save, `UnauthorizedAccessException` on writes, `DXGI_ERROR_DEVICE_REMOVED` mid-render.

All of these have the same underlying truth: **once state is inconsistent, the safest possible action is to stop, snapshot, and offer recovery.** Continuing risks compounding the corruption — and in the worst case, saving it to disk.

This plan introduces a single, narrow subsystem that any code can call to say "I've detected something that should not be possible," with a deterministic UX response that protects the user's work.

## Trip-source catalogue (motivating examples)

These all share the shape "state observed that violates an invariant the code depends on":

| Class | Example | Today's behaviour |
|---|---|---|
| Partial graph wiring | [Sentry TOOLL3-Y8](https://tooll.sentry.io/issues/7462814369/): `InputSlot.Input` null inside per-frame `InputUpdate`. The slot was constructed and its `UpdateAction` set, but the `Symbol.Child.Input` reference was never assigned before another slot's `ConnectedUpdate` reached through it. | Process terminates via unhandled NRE on the render thread. |
| Partial Symbol load | [Sentry TOOLL3-YE](https://tooll.sentry.io/issues/7514716102/): Symbol with null `InstanceType` reaches `EditorSymbolPackage.AllSymbolUis` and crashes `NamespaceTreeNode.PopulateCompleteTree`'s `OrderBy` lambda. | Process terminates before first frame. |
| Stale InstanceView after recompile | [Sentry TOOLL3-T8](https://tooll.sentry.io/issues/6875514056/): `InstanceView.Instance` can't resolve its cached `_symbolId` / `_instancePath` back to a live `Instance`. Documented to happen after recompilation when the parent re-creates its children — the `InstanceView` is now pointing at a discarded instance. **A previous attempt to suppress this throw at the access site (catch + return null in `ProjectView.CompositionInstance`) was reverted because masking the symptom there leaves every *other* consumer of the same composition — selection state, undo history, canvas-cached `Instance` pointers, parameter window targets — running on stale references, surfacing as a scattered range of secondary symptoms that are much harder to diagnose than the original throw.** A consistency-framework trip is the right response: detect at the InstanceView site, suspend interactions, offer recovery (which for this category may be as simple as re-resolving the composition path against the rebuilt symbol tree, or full restart). | Editor crashes on first frame after recompile. |
| Stale connection | A slot's `InputConnections[0]` points at an `InputSlot` belonging to a disposed instance. Same family as TOOLL3-T8 — every stale-reference site has the same problem: local recovery is meaningless because peers hold the same stale pointers. | NRE / crash in operator eval. |
| Stale undo target | [Sentry TOOLL3-YB](https://tooll.sentry.io/issues/7474137685/): `ChangeInputValueCommand.Undo()` looks up a guid (the original input target) in a dictionary that no longer contains it because the operator was removed / re-created between record and undo. Triggered here by `VariationBaseCanvas.TriggerThumbnailUpdate` calling `MacroCommand.Undo` to revert a thumbnail-blend macro after the underlying graph changed. Same stale-reference family as T8. | KeyNotFoundException terminates the editor. |
| Install corruption | Sibling plan: missing/quarantined TiXL files (assemblies, resources). Covered by [Plan_InstallVerificationAndSafeStartup](Plan_InstallVerificationAndSafeStartup.md) — detected proactively at startup, with the same recovery dialog primitives. | Verifier surfaces a clear "reinstall TiXL" dialog instead of opaque `FileNotFoundException` mid-frame. |
| Memory exhaustion | `OutOfMemoryException` allocating a vertex buffer during a render pass. | Unhandled — process terminates. |
| Disk failure mid-save | `IOException` / `UnauthorizedAccessException` writing a symbol file (disk full, OneDrive virtualisation, antivirus interference). | Save aborts partway, project files may be half-written. |
| GPU device loss | `DXGI_ERROR_DEVICE_REMOVED` ([Sentry TOOLL3-QA](https://tooll.sentry.io/issues/6598538703/), 56 hits; [TOOLL3-XC](https://tooll.sentry.io/issues/7299342442/), 4 hits — same category at different call sites). | Caught in some sites, unhandled in others; inconsistent recovery. |
| Hot reload race | An `AssemblyLoadContext` finishes unloading while the render loop is still calling into a now-disposed symbol package. | Various NREs / disposed-object exceptions. |

The framework below treats all of these the same way at the point of detection: report, suspend, recover.

---

## Design

### `Consistency.Report` — the single entry point

A static API in `Core/Diagnostics/Consistency.cs`:

```csharp
public static class Consistency
{
    public enum Severity
    {
        Warn,        // log only, do not trip
        Suspicious,  // log + telemetry, trip if a second report follows within N seconds
        Corrupt,     // trip immediately
    }

    /// <summary>Report a detected inconsistency. May suspend the editor.</summary>
    public static void Report(Severity severity, string category, string message, object? context = null);
}
```

`category` is a stable short string (e.g. `"InputSlot.WireOrder"`, `"Symbol.NullInstanceType"`, `"Save.IOException"`). Used for dedup, for the "this happened N times" UI, and for the framework's auto-mute on repeated reports.

`message` is free-form, written for a developer reading the log later.

`context` is anything with useful `ToString()` — typically `this` from the call site so the editor's Console window can navigate to the operator instance (same convention as `Log.Warning(..., this)`).

### Trip state machine

The framework holds a single global state — `Healthy`, `Suspended`, `RecoveryInProgress`:

- **Healthy**: normal operation; `Report` logs and may queue for the dedup timer.
- **Suspended**: rendering continues so the dialog is visible, but:
  - All `Window.DrawContent` calls skip everything except the active recovery dialog.
  - The undo/redo dispatcher is gated — no commands execute.
  - `T3Ui.Save` and autosave are blocked.
  - Operator evaluation is paused (the render callback short-circuits past `T3Ui.ProcessFrame`).
  - Keyboard / mouse input dispatch to canvases is suppressed.
- **RecoveryInProgress**: the user picked a recovery action; the framework is unwinding (saving snapshot, unloading packages, etc.) before the action's actual work.

Transitions:

- `Healthy → Suspended`: any `Severity.Corrupt` report, or two `Severity.Suspicious` reports of the same category within the configured window.
- `Suspended → RecoveryInProgress`: user picks Restart / Restore / Continue-at-own-risk.
- `RecoveryInProgress → Healthy`: only via "Continue at your own risk" (with a persistent banner). Restart and Restore both involve a process bounce / full reload, so they don't transition through `Healthy` in the same process.

### Recovery dialog

A modal owned by the framework (not by `WindowManager` — must work even when window state is suspect). Spec:

- **Title**: "TiXL detected an internal inconsistency"
- **Body**: free-form `message` from the first report that tripped it, plus the category and a "X more reports were suppressed" footer if any followed.
- **Auto-snapshot status**: "Snapshot saved to `<path>`" once step Snapshot completes (below). Greyed-out until then.
- **Actions** (in order):
  1. **Restore from backup** — opens the backup browser ([Plan_BrokenPackageRecovery Phase 2](Plan_BrokenPackageRecovery.md)) on whichever package contained the trip-source's `context`, or on all packages if context is ambiguous. On selection, restart with the restored state.
  2. **Restart TiXL** — clean restart, no restore. Snapshot is left on disk.
  3. **Continue at your own risk** — leaves a persistent red banner at the top of the editor with "Inconsistent state detected — saving is disabled" until restart. Saving remains blocked even after `Continue`; only `Restart` re-enables it. (Open question — see "Decisions still to make" below.)

### Auto-snapshot

Before the dialog is shown, the framework asynchronously writes a snapshot:

- Path: `<projectFolder>/.temp/Backup/<timestamp>-tripwire/` (same root as autosave).
- Contents: every editable package's current in-memory symbol state, serialised via the existing save path — but to the snapshot directory, not over the user's files.
- If a snapshot's own save throws, log it and proceed (don't recurse into `Report`).

The dialog's "Restore from backup" defaults to suggesting this snapshot as the most recent.

### Mute / dedup

A report fires the trip only on the first occurrence per `category` per session. Subsequent reports of the same category increment a counter that's surfaced in the recovery dialog footer. This prevents a tripwire in a per-frame loop from queueing thousands of reports.

### Telemetry

Each unique trip-source category is also forwarded to Sentry (one event per category per session) with the `category`, `message`, and `context.ToString()`. So a recurrence in the wild produces one Sentry report with diagnostic context, regardless of how many frames it fired.

---

## Phases

### Phase 1 — Framework, no integrations

**Goal**: `Consistency.Report` exists, the dialog opens, the editor suspends cleanly. No real call sites yet — verify with a debug menu action that calls `Consistency.Report(Corrupt, "Debug.TestTrip", "manual test")`.

**Tasks**:

1. Add `Core/Diagnostics/Consistency.cs` with the API surface above.
2. Add the global state machine + thread-safety (reports can come from the GC finalizer thread, ImGui thread, background save thread).
3. Add the suspend gates in:
   - `WindowsUiContentDrawer.RenderCallback` — short-circuit `T3Ui.ProcessFrame` when suspended.
   - `UndoRedoStack.AddAndExecute` and friends — bail on suspended state.
   - `T3Ui.Save` / `AutoBackup` — bail on suspended state.
   - Input dispatch in `MagGraphCanvas` and `Legacy` canvases — bail on suspended state.
4. Implement the recovery dialog. Use `BlockingWindow.ShowMessageBox` patterns where applicable, but the recovery modal needs to be hosted inside the ImGui render loop (so the user sees the editor's window with the modal overlay, not a separate WinForms dialog that may itself fail).
5. Implement the auto-snapshot writer.
6. Add a debug menu entry under `AppMenuBar` (DEBUG builds only) for manual tripping.

### Phase 2 — Integrate the known partial-state classes

Wire up trip points at the existing crash surfaces. Each one **removes** any local defensive null-guard or `try/catch` swallow and **replaces** it with a `Consistency.Report(Corrupt, ...)` call.

**Tasks** (one trip-source category each):

1. **`Symbol.NullInstanceType`** — in `Symbol.Namespace` getter and the LINQ enumeration at [NamespaceTreeNode.cs:69](Editor/Gui/Windows/SymbolLib/NamespaceTreeNode.cs:69). Detects a Symbol that survived load with null `InstanceType`. Resolves [Sentry TOOLL3-YE](https://tooll.sentry.io/issues/7514716102/).
2. **`InputSlot.WireOrder`** — in [InputSlot.InputUpdate](Core/Operator/Slots/InputSlot.cs:27). Detects `Input == null` at the per-frame entry. Resolves [Sentry TOOLL3-Y8](https://tooll.sentry.io/issues/7462814369/).
3. **`InstanceView.Stale`** — in [InstanceView.Instance](Editor/UiModel/ProjectHandling/ProjectView.InstanceView.cs:89) when `Symbol.TryGetOrCreateInstance` returns false. Replaces the current `throw new Exception(...)` with a framework report. The recovery handler for this category should attempt re-resolving the composition path against the rebuilt symbol tree before falling back to restart. Resolves [Sentry TOOLL3-T8](https://tooll.sentry.io/issues/6875514056/).
4. **`Command.StaleTarget`** — in `ChangeInputValueCommand.Undo` (and likely other command Undo/Redo paths) when the recorded guid target is no longer present in the current graph. Detect during dictionary lookup, report, suspend. The recovery handler can fall back to "discard remaining undo stack" rather than restart — the in-memory state is fine, only the undo history is stale. Resolves [Sentry TOOLL3-YB](https://tooll.sentry.io/issues/7474137685/).
5. **`Slot.StaleConnection`** — in `Slot<T>.ConnectedUpdate` when `InputConnections[0]` belongs to an instance that's been disposed.

Resolve each linked Sentry issue once its trip site lands.

### Phase 3 — Resource-exhaustion classes

These are detect-at-boundary, not invariant-violation. The framework treats them as `Corrupt` reports so the user gets the same UX.

**Tasks**:

1. **`Memory.OutOfMemory`** — top-level catch around `T3Ui.ProcessFrame` for `OutOfMemoryException`. Snapshot first, then report. The snapshot is best-effort under OOM (may itself fail; that's acceptable, the framework already handles it).
2. **`Save.IOFailure`** — in `T3Ui.Save` and `AutoBackup`. Catches `IOException` / `UnauthorizedAccessException`. The trip message should include the failing path so the user can see whether it's a permissions / disk-full / OneDrive issue.
3. **`Gpu.DeviceRemoved`** — `DXGI_ERROR_DEVICE_REMOVED` (and the `GraphicsCardSuspended` variant from TOOLL3-QA). Currently caught inconsistently; centralise here. Recovery action could include "Reinitialise GPU resources" as a fourth dialog button — stretch, deferred to Phase 4.

### Phase 4 — Recovery polish (stretch)

1. **Per-category "Continue at your own risk" rules**. Some categories (GPU device-removed) can recover cleanly without restart if we re-init the right resources; others (Symbol corruption) genuinely cannot. Encode this per category instead of a single global "Continue" button.
2. **In-place recovery without restart** for categories that support it. Shares lifecycle work with [Plan_BrokenPackageRecovery Phase 3](Plan_BrokenPackageRecovery.md#phase-3-restore-without-restarting-stretch).
3. **Tripwire timeline**. After a session ends with a trip, the next launch shows "Last session ended with a tripwire — view details / restore." Pairs with the existing `StartUp` crash dialog.

---

## Notes / risks

- **Suspending the wrong things.** Over-aggressive suspension (e.g. blocking *all* ImGui input) makes the recovery dialog itself unusable. The gates need to be at logical boundaries (save, undo command exec, operator eval) — not at `ImGui.Begin*`. Worth a manual test set that confirms the dialog is interactive under each trip category.
- **Reentrancy.** A `Report` call that happens *because of* the framework's own snapshot or dialog logic must not recurse. Guard with a thread-local "in framework" flag.
- **Finalizer thread reports.** The GC finalizer thread cannot show UI. Reports from a finalizer must queue and trip on the next render frame. The existing [Sentry TOOLL3-X5](https://tooll.sentry.io/issues/7251168817/) fix already swallows finalizer exceptions; once the framework lands, that becomes a `Consistency.Report(Suspicious, "Finalizer.Threw", ...)` instead of a silent swallow.
- **Telemetry duplication.** Sentry already auto-captures unhandled exceptions. The framework adds explicit categorised reports. Make sure we don't double-report — once a category is reported via the framework, the unhandled-exception fallback for that frame should be suppressed.
- **The "Continue at your own risk" trap.** If saving is silently disabled after a trip, a user who picks Continue may lose work by closing the app. The persistent banner needs to be unmissable, and `File → Save` needs to surface "Save is disabled — restart required" rather than silently doing nothing. **Open**: should `Continue` re-enable save after explicit user acknowledgement? Discussion needed.

---

## Manual test set

Add `.tests-manual/RuntimeConsistencyRecovery/` covering:

1. **Manual trip** — debug menu → "Trigger consistency tripwire". Verify dialog appears, save is blocked, undo is blocked, snapshot is created.
2. **InputSlot wire-order trip** — set up a graph mid-load using an injected delay; verify the trip fires with `category="InputSlot.WireOrder"`.
3. **Disk-full trip during save** — write to a 0-byte-free VHD-mounted folder configured as the project directory; verify `Save.IOFailure` trip with the failing path in the message.
4. **OOM trip** — allocate-until-fail in a debug-only operator; verify `Memory.OutOfMemory` trip.
5. **Recovery via Restore** — trip, pick "Restore from backup," verify restart restores the snapshot.
6. **Recovery via Continue** — trip, pick "Continue at your own risk," verify banner appears, save remains blocked, banner persists across windows.

---

## Out of scope

- Replacing every existing `try/catch` swallow in the codebase. This plan defines the framework and integrates the high-value sites; the rest is opportunistic cleanup as those code paths are touched in unrelated work.
- A "soft" mode where the editor tries to keep running through `Corrupt`-class reports. The whole premise is that corrupt state must stop.
- Cross-process recovery (e.g. handing off the in-memory state to a fresh editor process via IPC). The Restart action is a plain process restart.
- Automated bug-report submission. The user already has the Sentry integration; we just need to make sure the framework feeds it with categorised data instead of opaque unhandled exceptions.

---

## Order of work

1. **Phase 1** — framework + debug trigger. Self-contained, doesn't touch any existing call site, easy to revert. Ship this first to validate the UX.
2. **Phase 2** — wire the known partial-state classes. Each Sentry issue gets resolved as its trip site lands. Highest user-visible benefit.
3. **Phase 3** — resource-exhaustion classes. Less common but currently un-handled; meaningful coverage gain.
4. **Phase 4** — polish, in-place recovery, tripwire timeline. Only after the above are stable.

## Decisions still to make

- **Name.** `Consistency` / `Tripwire` / `Sanity` / `IntegrityMonitor`. Lean `Consistency` for now — neutral, matches the user-facing dialog wording.
- **"Continue at your own risk" semantics.** Soft-disable save (with banner) or hard-block until restart? Noted as an open question above.
- **Severity threshold for Suspicious-double-trip.** A second `Suspicious` report within 30s? 5s? Per category or global? Pick after we see Phase 2 in use.
- **What counts as `context`** for snapshot purposes. Currently "the package containing the failing thing" — but if the trip source is at the slot level, walking up to the package is non-trivial in some code paths. May need helpers.
