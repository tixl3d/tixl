# Bisect Protocol — Finding the Commit That Changed Runtime Behavior

Agent-neutral procedure for bisecting a runtime regression (frame timing, rendering, interaction) across
editor versions. Bisecting TiXL is useful and painful: old editors break on today's data, the debug
bridge does not exist before 2026-09, ops change between versions, and build outputs leak between
checkouts. This protocol is the version that worked; follow it instead of improvising.

## Roles

Split the work. Fully automated bisects (agent checks out, builds, launches, shuffles profile folders)
kept breaking on environment contamination and are not worth the risk.

| Human | Agent |
|---|---|
| Moves the TiXL profile folders aside once, before the first commit | Picks commits and explains why |
| Per commit: sets up the test scene, fixes broken ops, starts the measurement | Per commit: checkout, clean, restore, instrument, build, launch |
| Says when there are enough samples | Watches for results, reads them, picks the next commit |

The agent never renames, moves or deletes the user's profile or project folders.

## Tools

The tools live in [`bisect/`](bisect/) and run from a copy **outside** the working tree: every checkout of
an older commit deletes `.agentic/bisect/`, and `git clean -fdx` deletes it while it is uncommitted.

| File | Purpose |
|---|---|
| `install.ps1` | Copies the tools to `<repo>\..\_bisect` (or `-Target`) and writes `repo.txt` with the repository path |
| `prepare.ps1 <sha>` | The agent's per-commit step: checkout, clean, casing fix, restore, inject, Debug build, launch |
| `inject-probe.ps1 [-Label <text>]` | Instruments whatever is checked out; `-Label` tags a run such as `main` plus a candidate fix |
| `AgentFrameProbe.cs` | The probe source; commit info and output folder are substituted at injection |
| `wait-for-measure.ps1` | Blocks until a measurement has been appended and the editor has exited |
| `finish.ps1` | Forced checkout of `main` and probe removal; discards all tracked local changes |

Results accumulate next to the copied tools: `measurements.csv` (one line per phase) and `raw/`.

## One-time setup

1. **Install the tools** from `main`: `.agentic\bisect\install.ps1`. Re-running it updates the scripts and
   keeps earlier results.
2. **Profile isolation (human).** Move `%APPDATA%\TiXL*` and `Documents\TiXL*` away. Old editors load
   every user project they find, crash on ops they don't know, and write settings and `.csproj` files back.
3. **Close Rider's solution or expect lock failures.** Rider and file watchers hold handles that make
   `git checkout` fail to write `.git/index` and block folder renames.
4. **Decide the metric before the first sample.** One symptom per bisect. If the endpoints differ in
   several ways (in the 2026-10 run: vsync-on jitter *and* vsync-off throughput), bisect them separately.

## Per-commit loop

Agent, one step (`prepare.ps1 <sha>` from the installed copy):

1. `git checkout -f --detach <sha>` — forced; local edits from the previous round (op fixes) are disposable.
2. `git clean -fdx -e Installer/ -e .claude/ -e .idea/ -e .vs/` — removes all build outputs, but keeps the
   installer, agent permissions and IDE workspace settings.
3. Fix folder casing to match git (see pitfalls).
4. `dotnet restore t3.sln`.
5. Inject the measurement probe (below).
6. `dotnet build t3.sln -c Debug`, start `Editor/bin/Debug/<tfm>/TiXL.exe`.
7. Start `wait-for-measure.ps1` in the background; it returns once a new result exists and the editor has
   exited, so the human doesn't have to report each round.

Human:

8. Create or open the single test project, delete everything except the op under test, pin it.
9. Fix the op if this version breaks it (connections to removed slots are typical). Note the fix —
   it is part of what was measured.
10. Set output resolution and playback state the same way every round.
11. Press **Start Measure**. The editor closes itself when done.

Agent, on the watcher firing: read the result, classify good/bad, pick the next commit, go to 1.

## The measurement probe

Inject instead of relying on built-in metrics: the Performance window and the debug bridge both appeared
during the period being bisected, and their semantics changed. The probe is one file plus three
one-line hooks found by regex, so the same source works on every commit of the year:

- `Editor/AgentFrameProbe.cs` — global namespace, editor types reached by reflection (`T3Ui.UseVSync` is a
  field in old versions and a property later), commit sha/date/subject baked in at injection.
- `WindowsUiContentDrawer.cs` — `AgentFrameProbe.OnNewFrame();` before `ImGui.NewFrame();`, and
  `BeforePresent()` / `AfterPresent()` around `ProgramWindows.Present(...)`.
- `AppMenuBar.cs` — `AgentFrameProbe.DrawMeasureButton();` after `T3Metrics.DrawRenderPerformanceGraph();`.

The button runs: 2 s settle → 10 s vsync on → 2 s settle → 10 s vsync off → restore vsync. It appends one
CSV line per phase to `measurements.csv` (frames, interval mean/median/p95/p99/max/std, hitches = frames
above 1.5× median, CPU work mean, Present mean/p95), writes per-frame samples to `raw/`, logs
`Measure complete …`, and calls `Environment.Exit` after a second. Exiting skips the editor's settings
save on purpose, so the measuring session leaves nothing behind.

Frame **interval** (NewFrame to NewFrame) is the primary signal; **work** (NewFrame to Present) and
**Present** duration tell CPU cost from presentation stalls. For jitter, the interval std and p95 separate
good from bad far more clearly than the mean, which vsync pins to the refresh period.

Never commit the probe. Remove it before returning to `main`.

## Picking commits

- Measure both endpoints with the probe first; numbers from other tools are not comparable.
- Bisect along `git rev-list --first-parent` of `main`. Feature branches arrive as one merge commit:
  test the merge and its first parent to decide whether a branch is involved at all before bisecting
  inside it.
- Read `git show --stat` of the remaining candidates. Commits that only touch `.help/`, `.tests-manual/`
  or plans can't change runtime behavior; measuring their neighbor decides two candidates at once.
- A plausible suspect is worth testing directly when it sits near the midpoint; otherwise take the midpoint.
- Keep known-good and known-bad as explicit shas in your notes after every round.

## Pitfalls seen in practice

| Symptom | Cause | Handling |
|---|---|---|
| Old editor logs `Registry already contains id …`, then crashes (`Requested value 'X' was not found`) | Ignored `Operators/*/.temp/bin/<cfg>/SourceCode/*.t3` copies from newer builds survive checkouts and are scanned as symbols | `git clean -fdx` every round, never a plain checkout |
| Packages loaded that don't exist at that commit | Fully ignored leftover folders (`Operators/Io`, `Video`, …) | Same clean |
| Folder casing differs from git (`examples` vs `Examples`) | Windows checkouts do not apply case-only renames | Rename via a temporary name; retry, a watcher may hold the folder |
| `fatal: unable to write new index file`, thousands of changes, HEAD unchanged | Another process held `.git/index` mid-checkout | Retry `git checkout -f`; the forced retry repairs the tree |
| `Unable to create '.git/index.lock': File exists` | A crashed or killed git call left the lock behind | Only if no `git` process is running: delete `.git/index.lock`, retry |
| Project unreadable after going back in time | Newer editors migrate projects one way (e.g. the 2026-08 `Symbols/` folder format) | Recreate the test project in the older version, or keep a pre-migration copy |
| Op renders nothing or errors | Slots or ops changed between versions | Human fixes it in place; the next forced checkout discards the fix |
| PowerShell script aborts on a harmless git warning | Windows PowerShell treats native stderr (CRLF warnings) as errors under `ErrorAction Stop` | `git -c core.safecrlf=false …` in scripts |
| Build fails or gutted a running editor | Editor build wipes `bin/<cfg>` | Never build while `TiXL.exe` runs |

## Finishing

1. `finish.ps1`: back on `main`, probe removed, tree clean.
2. Human restores the profile folders.
3. Verify the fix with the same probe: apply it on `main`, run `inject-probe.ps1 -Label "fix1 …"`, build,
   measure, and compare against the last good commit. Remove the injection by reverting
   `AppMenuBar.cs` and `WindowsUiContentDrawer.cs` and deleting `Editor/AgentFrameProbe.cs`;
   `finish.ps1` would discard the fix too.

## Worked example: frame jitter, 2026-10

Symptom: with vsync on, simple scenes alternated between ~0 ms and ~31 ms frame intervals (std ≈ 10 ms,
~200 hitches per 600 frames) although CPU work was ~3 ms. Eleven measurements across 2025-07 … 2026-10
narrowed it to `b92bde57a`, which stopped presenting the hidden Viewer swap chain every frame. That extra
`Present(1)` had been masking Main's frame-latency waitable running with `MaximumFrameLatency = 2`.
Setting it to 1 measured std 0.075 ms and no hitches. A docs-only neighbor (`737325953`) decided the last
two candidates in one round.
