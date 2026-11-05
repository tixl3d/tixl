# Output Setup: Architecture Review

Reviewed at commit `b9c2845ad`. Procedure: `.agentic/Plans/output-setup/architecture-review-plan.md`. Nothing was fixed.

**How this was checked:** by reading the code only. Nothing was run, because no editor was open and the user launches it.
No failure below has been seen in a running editor.

- **VERIFIED:** a second reader confirmed the path directly in the code during the merge.
- **TRACED:** one reviewer followed the path in the code. It is not independently re-checked.
- **SUSPICION:** the path is plausible, but a link in it is unconfirmed.

Paths: `OutputSetup/` means `Editor/Gui/Windows/OutputSetup/`.

## Summary

The surface-to-patch bug has relatives. The same pattern appears several times: a fact is stored once, but another
piece of code or another host treats it differently, and the gap only shows when two features meet.

The five things to act on first:

1. **Combining a `SendToOutput` destroys its routing, with no undo** (finding 1). The same sweep breaks Ctrl+Z after a
   graph delete. This is the most likely real-world data loss.
2. **Three menu or drag actions crash the editor** by changing a list while drawing walks it (finding 2). These are the
   same class as the Alt+drag crash, and each has a small fix.
3. **A setup file can be silently overwritten or lost** through a mismatch between the file name and `Name`, non-atomic
   saves, and deleting with no confirmation (finding 3).
4. **The editor accepts pins the loader then replaces** and saves over the calibration (finding 4).
5. **The Player can show a different picture from the editor:** linked pins on fitted patches, a shader probably missing
   from exports, sends outside the export root, and the calibration raster (findings 6 and 9).

Format changes are cheap only until release. They are collected under "Before release".

## Findings

### Data loss or wrong output

#### 1. A send's child id is treated as permanent, and a sweep deletes routing when it changes (VERIFIED)

**Status:** fixed after this review with decision 1(a), but not yet tested in the editor. A source with slices and no op now stays and reads as "op missing", with a Remove
command, and `Combine` remaps the ids in every setup of the project. A source with no slices still goes on its own. Missing now means missing from the focused project, and a missing item can be
connected by dropping a send on it or by **Create SendToOutput**, which also covers cut and paste and setups copied between projects. Open: missing sources have no Board card.

- **Assumes:** a child id that no loaded symbol lists means the user deleted the op, so its routing can go
  (`Core/Output/ContentSource.cs:10-14`, `OutputSetup/ContentSourceSync.cs:415-471`).
- **True:** `Combine` copies children under new ids and deletes the originals (`Editor/UiModel/Modification/Combine.cs:218,309`).
  Nothing maps old ids to new ones. The sweep runs outside undo and saves after one second (`ContentSourceSync.cs:370-377`).
- **Failures:**
  - **Combine:** route a send to surfaces and patches, then **Combine** it. The source and slices go and the picture
    turns black. `Combine.cs:323` has cleared the undo stack, so nothing brings it back.
  - **Graph delete and Ctrl+Z:** the op returns with its old id, but the sync adopts a new, empty source. Only
    `ContentSourceSync.DeleteSendOp` pairs a setup snapshot with the graph command. Cut and paste loses routing too.
  - **Other setups** are swept when they are switched to, because `_sweptSetupId` changes (`:360`).
  - **Dangling feed:** the sweep clears `surface.SliceId` but not `patch.SliceId` (`:436-440`), unlike
    `SetupActions.DeleteSliceInternal` (`SetupActions.cs:993-1011`). `PatchHasInputs` then reports input that doesn't
    exist, and dropping a surface on that patch deletes the patch and copies the dead id (`SetupRouting.cs:162-170`).
  - **SUSPICION, missing package:** the check is "no loaded symbol lists it", not what its comment says (`:457`). A
    project whose owning package failed to load may lose routing on the first frame (`_sweptRegistryVersion` starts at -1).
- **Root cause:** `ContentSource` mirrors the graph and also owns routing, and its lifetime follows the graph through a
  destructive, unrecorded sweep.
- **Change (small to medium, no format change):**
  - Stop cascading from the sync. A source without an op stays in the file, shown as missing, with an explicit Remove.
    This alone repairs Ctrl+Z and the missing-package case, because adoption already matches by `SymbolChildId`.
  - `Combine` remaps through `OldToNewChildIds` in every setup file of the project.
  - Keep one cascade: `DeleteSliceInternal`.

#### 2. Structural edits run while draw code walks the list: three crashes (VERIFIED)

**Status:** the three crashes were fixed after this review with the minimum local changes, but not yet tested in the editor. The root cause and the
recommended `SetupEdits.Defer` remain open.

- **Assumes:** a menu item or drop can change the model where it is drawn.
- **True:** the frame only catches `SEHException` (`WindowsUiContentDrawer.cs:224`), so a managed exception here takes the
  editor down with any unsaved graph work.
- **Failures:**
  - **Pin to Patch on a patch with content:** a `foreach` over `Patches` (`SetupEntityContextMenu.cs:360-372`) reaches
    `Patches.RemoveAll` (`SetupRouting.cs:170`). Right-click a surface, then Pin to Patch and pick a fed patch.
  - **A "not attached" display row:** a `foreach` over `Bindings` (`SetupFlowOutliner.cs:600-613`) draws a row whose menu
    or drop calls `Bindings.RemoveAll`. Bind to Display 3, open with two displays, then choose Unbind on that row.
  - **Closing a room from its first corner:** the corner loop has a cached `count` (`SetupOutputView.Board.cs:1050`) and
    no `break` after `CloseByMerging` removes `Vertices[0]` (`:1093`, `FloorPlanSync.cs:499`). At the last index,
    `plan.Vertices[i]` is out of range. Dragging the last corner onto the first is fine.
- **Root cause:** there is no rule for when structure may change. Each action decides for itself (`_regionToCopyId`,
  `_pendingMove`, index loops with comments).
- **Change:** the minimum is about 10 lines: defer the first, use an index loop in the second, `break` in the third.
  Recommended is about 40 lines plus call sites: `SetupEdits.Defer(Action)`, drained at the top of
  `OutputSetupHandling.UpdateFrame` and used by menus, drops and setup switching.

#### 3. Setup files can be overwritten or lost (VERIFIED for the first two, TRACED for the rest)

**Status:** fixed after this review, with unit tests for the file behaviour but no test in the editor. The name now comes from the file name, a copied file gets a fresh `Setup.Id` when switched to directly
from its original, saves are atomic, an unreadable setup is left in place and a default never takes its name, an unreadable machine file is copied to `.unreadable`, and Delete asks first.
Open: no `.bak` of earlier versions, and the fallback after an unreadable active setup is reported only as a log warning.

- **Name vs file:** the switcher lists file names, but `SaveActive` writes to the JSON `Name`
  (`OutputSetupHandling.cs:94,143-146,155-160`). Copy `Hall.setup.json` to `Hall_old.setup.json` in Explorer and pick
  `Hall_old`: `Hall.setup.json` is overwritten with the old content at once, and the session now edits `Hall`.
- **Delete:** one menu click runs `File.Delete` with no confirmation and no undo (`SetupFlowOutliner.cs:991-995`,
  `OutputSetupHandling.cs:243-244`). A venue's whole calibration goes.
- **Non-atomic saves:** `File.WriteAllText` truncates first (`Setup.cs:426`, `MachineConfig.cs:129`), and every edit saves.
- **Silent fallback:** a parse failure logs a warning and loads the first other file (`SetupFiles.cs:44-60`). The next
  save records that file as active, possibly within a second of opening (`ContentSourceSync.cs:370-376`).
- **All files bad:** the unsaved default "Setup 1" can overwrite a damaged `Setup 1.setup.json`.
- **Bad machine file:** it becomes an empty config (`MachineConfig.cs:147-151`), and the next save erases the bindings and
  stream plugs.
- **Root cause:** the file name and `Name` are two owners of one fact, and the file is treated as always healthy.
- **Change (about 30 lines, no format change):** set `Name` from the file name after loading, and give a fresh `Setup.Id`
  when two files share one. Write to a temp file and replace, keeping `.bak`. On a parse failure, keep the file as
  `.corrupt` and warn visibly. Confirm a delete or move the file to a trash folder.

#### 4. Validity is defined only at load, so an edit can produce a pin the loader replaces (TRACED)

- **Assumes:** `SetupRepair` only meets hostile input (`Core/Output/SetupRepair.cs:10-14`).
- **True:** `QuadIsUsable` rejects corners outside -3..4, and nothing else calls it. The corner drag has no clamp
  (`SetupOutputView.cs:874-888,909`). `SurfaceGeometry.CarryQuad` extrapolates with no range or finite check (`:196-203`).
- **Failure:** extend an edge of a strongly keystoned pin, or drag a corner far out. It looks fine and is saved. Reopen
  the project or switch away and back: the pin is replaced by a default quad with only a log line, and the file is
  overwritten (`OutputSetupHandling.cs:339-343`). The calibration can't be recovered.
- **Same cause, NaN:** a non-finite corner is written as the string `"NaN"`, and `JsonUtils.SafeFloatFromArray` reads a
  string as 0 (`Serialization/JsonUtils.cs:202-211`, VERIFIED). The corner silently becomes (0,0), the quad now passes, and no
  warning appears. Live state, undo and reload all differ. The writer behaviour is Newtonsoft's default, from memory.
  `SetupRepairTests` builds NaN on objects and never goes through JSON. Repair also skips `Trace.Quad`,
  `QuadBeforeAligning`, `PointAims`, `Placement` and floor plan vertices.
- **Change (small, no format change for valid files):** use one validity check at `CommitGesture` and in `CarryQuad`. Read
  non-finite strings as NaN so repair fires. Add a JSON round-trip test. Copy the file to `.bak` before writing a repair.

#### 5. A region with its own pin vanishes from the parent's other outputs (VERIFIED)

- **Assumes:** a region's mapping "overrides that for that one output" (`Core/Output/Surface.cs:33-35`).
- **True:** `Setup.FindMappedAncestor` stops at the first surface with any mapping (`Setup.cs:105-121`), and the compositor
  walks only that surface's mappings (`OutputCompositor.cs:158,182-185`). `SurfaceGeometry.FindMappingCarrier` does the same
  (`:211-215`).
- **Failure:** map a parent to O1 and O2, then drop its region on a patch of O2. The region disappears from O1.
- **Change (small, no format change):** find the carrier per output, walking up until `HasMapping(outputId)`. Add a test.

#### 6. A linked pin stores a derived quad, and only the editor keeps it current (VERIFIED)

- **Assumes:** `mapping.Quad` is valid wherever it is read.
- **True:** the compositor and `StageGeometry` read it (`OutputCompositor.cs:187`, `StageGeometry.cs:280-283`). The Player
  re-fits patches at load (`Player/Program.cs:567`, `SetupFiles.cs:143-148`), but the sync exists only in the editor
  (`SetupRelations.cs:158`, `OutputSetupHandling.cs:45,92`).
- **Failures:**
  - **Player:** link a wall to an unfed fitted 16:9 patch on an output with canvas 0×0, saved at 1920×1080. Export as Demo and
    pick 1280×1024. The patch refits to Y 0.148..0.852, but the wall still fills the canvas. The editor letterboxes.
  - **Editor:** `UpdateFrame` re-fits without `NotifyChanged` (`OutputSetupHandling.cs:44-47`), so after a display mode
    change the `StageGeometry` mesh stays stale until the next edit.
  - **Shared file:** fitted quads are saved (`OutputDefinition.cs:228`), so one machine's aspect dirties the file for all.
    `AspectRatio` and `Scale` are written only when fitted (`:232-237`), so Fit, Stretch, undo, Fit loses a custom 2.39:1.
- **Change:**
  - Now, small: move the sync into Core at the end of `SetupFiles.ResolveCanvasResolutions`, notify when a fit changed
    something, and always write `AspectRatio` and `Scale`.
  - Before release, recommended: linked pins and fitted patches stop saving `Quad`, and readers resolve through `PatchId`.
    Old files stay readable. `SetupRepair.cs:91` must then skip fitted patches.

#### 7. One choice about a pin is spread over several fields (TRACED, re-link rule VERIFIED)

A pin is filled, free or linked, but that is stored across `Mode`, `PatchId`, `Quad` and `QuadBeforeAligning`.

- **Invalid state:** `RestorePinBeforeAligning` restores `Quad` and `Mode` but not `PatchId`
  (`SetupOutputView.PinCalibration.cs:192-194`), and a drop leaves the backup in place (`SetupRouting.cs:149-163`). Aim a
  point on a Fill mapping, drop the surface on an empty patch, then press Reset Alignment. The state is now Fill and linked.
  The editor pulls the quad back to the patch each frame, so Reset appears to do nothing. In the Player, repair sets the
  full canvas and nothing syncs, so the wall fills the canvas.
- **Links end and return by side effect.** `LinkPinsSavedWithoutPatchIds` runs on every load and persists
  (`Setup.cs:455`, `SetupRepair.cs:100`), and `Setup.Version` is always 1. It re-links in these real cases:
  - Pressing a corner without moving it: `Started` counts as a phase (`CanvasPointHandle.cs:87`), so `PromoteToCornerPin`
    runs (`SetupOutputView.cs:637`). A click ends the link for this session; the next load restores it.
  - Reset Alignment on a pin that was linked when first aimed.
  - The deliberate case: drop a wall on a fed patch or use **Use on Surface**, then **Split 2×2** again. The free,
    calibrated pin links to the new tile on reload, and moving that tile later drags the wall.
  - Fill, then Corner Pin, on an output with an empty full-canvas patch.
- **Reset jump:** `CarrySpaceChange` doesn't carry `QuadBeforeAligning` (`SurfaceGeometry.cs:171-180`). Align, crop an edge,
  then Reset, and the picture jumps.
- **No way to unlink:** there is no explicit unlink command.
- **Change (small to medium):** one `LinkToPatch` setter that clears the backup and aims. Promote on `Dragging`, not
  `Started`. Reset restores the link or clears it. Repair: Fill clears `PatchId`. Add an Unlink command. See decisions 3 and 4.
  A cleaner form, cheap before release, is a `Linked` value in `MappingModes`.

#### 8. Width and anchor edits bypass what the rest of the model relies on (TRACED)

- **Wall width has writers that skip the plan.** `FollowWallWidth` is skipped by the Board corner scale
  (`SetupOutputView.Board.cs:540`), group edge scale (`:762`), Apply Lengths (`SetupOutputView.Measure.cs:464`) and Set
  Measured Size (`SetupParameterView.cs:1558`). Set a plan wall from 3 to 3.2 m, then later move a different corner: `Apply`
  sees the mismatch (`FloorPlanSync.cs:47`) and crops 0.2 m off one end, moving the pin and slice UV.
- **Plan floors:** a Board edge crop returns early in `FollowWallWidth` (`:89`), and the next `Apply` re-meters it away.
- **Size tooltip:** it says lines and regions scale (`SetupParameterView.cs:304`), but a typed wall width goes through
  `CropWall`, which scales nothing.
- **Anchor field:** `SetupParameterView.cs:380` writes `Anchor` directly, but regions, lines, raster and pose are measured from
  it. Change it and regions shift on the wall, reference points name other physical spots, and the surface moves in the
  stage. A plan wall jumps at the next `Apply`. `SurfaceGeometry.cs:12-15` promises the opposite for bounds.
  `FloorPlanSync.cs:237` has the same issue for height once a crop has moved the anchor off the bottom.
- **Change (small, no format change):** one `SetSurfaceWidth(setup, surface, width, mode)` for all writers. Redirect floor
  crops to `SetFloorSize`. For the anchor, see decision 6.

`Apply` and `FollowWallWidth` do not fight each other. The problem is only the writers above.

#### 9. The exporter never compares the setup with what it ships (shader SUSPICION, rest TRACED; raster VERIFIED)

- **Shader:** `ProjectOntoStage` loads its shader from a code constant (`ProjectOntoStage.cs:30-31,247`). The exporter
  collects string file inputs plus a fixed list (`PlayerExporter.cs:146-154`, `:657`), and the list doesn't include it. The
  bytecode cache is exported, but `TryGetShaderFromFile` takes a `FileResource`, so the source file is probably still
  required. Expect an empty canvas in the Player. **Confirm with one export.** `SkinMesh`, `RenderToCubeMap` and
  `BindToSkeleton` look like the same class (checked by grep only).
- **Sends outside the export root:** the setup can route them, and the patch or surface is then black with no warning
  (`OutputContentResolver.cs:213-215`, `OutputCompositor.cs:123`).
- **Wrong package folder:** the setup is read from `symbol.SymbolPackage.Folder` (`PlayerExporter.cs:537,748`), while
  `GetExportDirectory` knows a child's symbol may live in a library. That export silently ships the default setup.
- **Missing stream package:** skipped with no log (`:547-549`).
- **Raster in the show:** `ShowGrid` is saved and drawn with no overlay check (`OutputCompositor.cs:210`), though the comment
  at `:74-78` says calibration aids exist only while authoring. Leave it on, export, and the grid is on the wall.
- **Change (about 60 lines, no format change):** record the addresses each owner requested and export those. Use the owner's
  package. Add an export preflight that warns per unresolved route and stream. Use `surface.ShowGrid && Overlay != null`, or
  make the raster session state.

#### 10. `StageGeometry` and name matching can place a wall on the wrong pixels (TRACED)

- **Other output's coordinates:** `TryGetCanvasQuad` checks the mapping on the chosen canvas, then a same-name patch, then the first
  mapping on *any* output (`StageGeometry.cs:274-296`). With wall A on P1, wall B on P2 and Canvas = P1, wall B uses P2's quad
  as coordinates into P1's picture. It counts as placed, so no warning appears. A deleted output or a picked reference image
  takes the same path, because the picker offers images to every op (`GuidInputUi.cs:59-61`).
- **Name collision:** default wall names come from the segment index (`FloorPlanSync.cs:273`) and can repeat
  (`InsertVertex`, `:430-434`; two plans both raise "Wall 1"). Matching is case-insensitive, first match (`StageGeometry.cs:345`).
  With a pixel map of "Wall 1..4", insert a corner on edge 2: the new half wall shows Wall 3's area.
- **Rename:** no cascade or warning (`SetupEntities.cs:163-201`). The warning is suppressed when nothing else is placed
  (`StageGeometry.cs:191`). `FloorPlanSync.IsInUse` flips too (`:583`), so unticking an edge can delete the wall instead of
  lowering it.
- **Two rule sets:** `SetupRelations.TryFindCanvasQuad` and `StageGeometry.TryGetCanvasQuad` order these checks differently.
- **Status:** name matching is a documented feature, so it isn't a rule violation in itself, and
  `long-term-features.md:61-74` already plans its retirement. The planned "Connect by Name" can't reuse the drop, because a fed
  patch is deleted by it (`SetupRouting.cs:169-170`).
- **Change:** drop the any-output fallback when a canvas id is given, warn when it doesn't resolve, narrow the picker per op,
  and share one implementation. See decisions 2 and 7.

#### 11. A display is identified by its position in the arrangement (TRACED)

- **True:** only `DisplayIndex` resolves (`Plugs.cs:24`, `OutputPresentation.cs:59`, `Player/Program.OutputWindows.cs:53-60`).
  `DisplayName` is stored and never read, and `PlugBinding.cs:11-12` says the opposite of `:33-36`.
- **Failure:** with projectors at index 1 and 2, unplug the first. Output A now shows on the projector meant for B, with no warning.
  Only an index past the end is reported (`SetupFlowOutliner.cs:602`).
- **SUSPICION:** the editor enumerates `Screen.AllScreens` and the Player GLFW monitors (`Plugs.cs:60`, `SilkWindowProvider.cs:78`).
  If the orders differ, an Installation presents on the wrong projector. Log both on a machine with three displays and a
  non-first primary.
- **Change:** small: warn when the recorded name differs from the one at that index, and fix the doc. Medium: one inventory
  and a stable display key, in the machine file only. The SDL3 port needs this anyway.

#### 12. Bindings are keyed by output id across all setups (TRACED)

`Setup.Duplicate` keeps output ids and the machine file has one list.

- Duplicate a venue, delete P2 in the copy, and switch back: P2 in the original is unbound (`SetupActions.cs:1049`).
- Binding Display 2 in setup A unbinds setup B's output on that plug (`Plugs.cs:90-97`).
- Delete an output and undo: it returns unbound.
- **Change (small, no format change):** stop unbinding on delete. Orphan bindings are already harmless
  (`SetupRouting.cs:232-235`, `OutputPresentation.cs:40-46`). Limit the release in `BindOutput` to the active setup's outputs.

### Silent surprise

#### 13. Undo ignores which setup is active (TRACED)

- `SetupSnapshotCommand` no-ops on another setup but is still popped (`:76-81`, `UndoRedoStack.cs:63-65`). Edit in A, switch to B,
  press Ctrl+Z: nothing visible happens, and any new edit loses A's step for good.
- A macro half-applies: delete content in A, switch to B, Ctrl+Z restores the op but not A's routing.
- **Narrow data loss:** `CommitGesture` has no setup check (`SetupUndo.cs:33-42`). Hold the gamepad stick so a gesture is live
  (`PinGamepad.cs:101-105`), switch setups with the mouse, and the old JSON commits under the new id. A later undo replaces venue
  B's lists with venue A's.
- Ctrl+Z isn't blocked during a gesture (`T3UI.cs:46-48`), so `_gesture.OldJson` goes stale and `SurfaceRectSnapshot._marks` holds
  dead objects.
- **Change (small):** store the setup id with every held snapshot, drop a commit on a mismatch, end live gestures when the setup or
  `StructureVersion` changes. See decision 8.

#### 14. The implicit patch's role changes behaviour, not just listing (TRACED)

The role is index 0, unnamed, within 0.001 of the canvas, unturned and unfitted (`SetupRelations.cs:244-259`), and
`FeedOutputDirectly` and `ClearOutputInputsInternal` fork on it (`SetupRouting.cs:270-276`, `SetupActions.cs:1263`).

- **Failure:** an output shows A on its implicit patch. Nudge one corner slightly, then drop B on the output. B goes under A and shows
  only in the sliver A left. **Clear Inputs** no longer clears A either.
- **Narrow:** clearing inputs removes the implicit patch while a linked pin may remain (only reachable through finding 7's re-link).
- The implicit **slice** is fine: it affects display and defaults only.
- **Change:** see decision 5.

#### 15. Duplicating a surface leaves parts pointing at the original (TRACED)

- `DuplicateChildrenOf` keeps a region's own pin, `PatchId` and quad (`SetupActions.cs:1205-1216`), while the root copy is unlinked and
  nudged (`:1185-1196`). Moving that patch moves the original and the copy's region.
- Duplicating a region nudges only `LocalPosition` (`:1177-1181`), so its own pin stays on top of the original.
- The nudge skips `PointAims` and `QuadBeforeAligning`. Marks disagree with the pin by 1.25% of the canvas, and Reset jumps it to the original's place.
- The copy keeps `BoardPlacement`, so menu Duplicate hides it under the original on the Board, despite the comment at `:1168`.
- An unnamed output's copy is named `" copy"` (`:1094`), which ends its derived label.
- **Change (small):** route every pin in `CloneSurface` through one detach step, and nudge the Board position too.

#### 16. A linked wall's metres remap its content (TRACED; intent is decision 3)

`CarrySpaceChange` rewrites the pin without promoting it (`SurfaceGeometry.cs:179`), and the sync puts it back on the tile. Drag the plan
so a linked 4 m wall becomes 6 m: regions compress on the tile, and a shown slice is cropped and stretched over it. The same edit on an
unlinked wall moves nothing.

#### 17. Several smaller inferences and surprises (TRACED)

- **`ApplyWallHeight` infers overrides by comparing values** (`FloorPlanSync.cs:223-241`). With plan height 4, set a wall to 3, then the
  plan to 3, then to 5: the wall jumps to 5. `FloorPlan.cs:98` says walls keep their own. Store overrides or stop following.
- **One send, several instances:** only the first registered renders (`OutputContentResolver.cs:258-267`), the outliner lists both under
  one id, and `IsSourceInScope` tests only the first (`ContentSourceSync.cs:76-77`). Prefer an in-scope instance and warn on the op.
- **Update Slices depends on what regions show now.** Re-routing one region to another source turns Update into Create, adding a new send
  (`RegionSlices.cs:40-41`, `SetupEntityContextMenu.cs:123-128`). A hand-given name is overwritten here (`RegionSlices.cs:109`) but kept by
  Wallpaper (`WallpaperLayout.cs:183`). A region past a shortened wall gets a squeezed UV. Density reads only the region's own mapping
  (`SetupRelations.cs:379-395`), so a riding region falls back to 400 px/m.
- **`BoardPlacement` decides content layout:** Wallpaper orders walls not on a plan by card X (`WallpaperLayout.cs:262,337-340`), against
  `BoardPlacement.cs:11`.
- **Move Origin Here uses a stale point:** `PlanMenuPointInPlan` is set only by a Board right-click (`Picking.cs:122-126`), but the entry also shows
  in the outliner menu (`SetupEntityContextMenu.cs:184-187`). It can re-express the whole setup around an old point. Undoable.
- **Opening writes files:** adoption auto-saves (`ContentSourceSync.cs:323-338,370-376`), against `OutputSetupHandling.cs:14-16`. Merely opening a
  project with a send writes the setup, the machine file and this machine's fit.

### Fragility

- **Stale docs and dead code:** `TraceBinding.PatchId` (`Surface.cs:182-184`, no reads, writes or JSON). `PlugBinding.cs:11-12`. `FloorPlan.cs:15`.
  `OutputDefinition.cs:108-109` ("never both" contradicts linked pins). Removed `TargetIds` in `SetupActions.cs:789-792`,
  `SetupParameterView.cs:152` and `ListInputValueUi.cs:301`. Orphan comment `SetupRelations.cs:419`. `ContentSourceSync.cs:457`.
  `OutputCompositor.cs:248-249` (untrue for `RenderPreview` from draw).
- **Raw `output.Name`:** Player window titles and logs (`Player/Program.OutputWindows.cs:55,67,71,75`), `SetupRepair.cs:70,85,94`, and
  `SetupEntityContextMenu.cs:349` show `""` for plug-created outputs.
- **`CancelGesture` drops the undo step but keeps writes:** seeded `PointAims` ride along in the next step (`PinCalibration.cs:64-70,95-103`).
- **Inert leftovers worth one line each:** `output.ReferenceImageId` after deleting the image (`SetupActions.cs:952-961`; all readers are null-safe).
  `Lowered` and `WallElevations` on a duplicated plan (`:1134`). Orphan `PointAims` keys.
- **SUSPICION, neighbour walls:** during a live edge drag a neighbour is cropped incrementally, so dragging through `MinSize` and back may not restore its
  slice UV until undo.
- **SUSPICION, rename:** "Rename" on a kind with no outliner row never clears `_renamingId`, and `OutputSetupModeView.cs:58` keeps forcing the strip open.
- **Structure:** size itself is not the risk. The risk is structural decisions inside per-handle draw code (finding 2) and no test path to them. `FloorPlanSync`
  and `SetupRelations` use no ImGui but are `internal` with no `InternalsVisibleTo`. `SetupRouting`, `SetupActions`, `RegionSlices` and `WallpaperLayout` go through
  `SetupUndo` to `UndoRedoStack` and `ProjectView.Focused`. Exposing the `*Internal` methods and injecting the commit is the smallest test boundary.

### Cost

**Undo snapshots:** cost is per gesture, not per frame: `ToJsonString` twice plus a synchronous disk write (`SetupUndo.cs:19,35,41`). The "few KB" comment understates it:
an estimated 2-3 KB per surface gives about 500 KB per step at 100 surfaces, and the stack is unbounded. This is an estimate, not a measurement. Cap setup steps or share strings
when old equals the previous new.

## Decisions for the user

Each lists a recommendation first.

1. **A source whose op is gone.** (a) Keep it and its routing, shown as missing. (b) Keep cascading and also wrap every graph delete. Recommend (a).
2. **Name-matched walls.** (a) Store `Surface.CanvasPatchId`, with a one-time name-to-id step, then drop the fallback (medium, additive). (b) Keep names, with unique defaults, cascaded
   renames and an always-on warning (small). Recommend (a) if pixel-map shows are a release use, otherwise (b) now.
3. **Resizing a linked wall.** (a) The tile wins: document it and skip UV anchoring and carries while linked. (b) Resizing ends the link. (c) Store the crop as a sub-rectangle of the tile.
   Recommend (a): a link means "this wall is that tile".
4. **`LinkPinsSavedWithoutPatchIds`.** (a) Delete it, if no venue file predates `PatchId`. (b) Bump `Setup.Version` to 2 and run it only below 2. Recommend (a) if you can confirm that, else (b).
5. **Implicit patch.** (a) Store `Patch.IsBase`, set when the full-canvas route is created, and infer it once for existing files. (b) Keep the inference for listing, and have Feed and Clear find the
   base by id. Recommend (a).
6. **Anchor edit.** (a) A pure origin move: re-express regions, lines, pose and card so nothing moves physically (small). (b) Keep it and say in the tooltip that content moves. Recommend (a).
7. **`StageGeometry` with an unresolved or unmapped canvas.** (a) Warn and leave the surface off-canvas, keeping the fallback only for an empty id. (b) Keep the fallback. Recommend (a).
8. **Undo across setups.** (a) Clear setup steps on a switch, with a notice (smaller). (b) One stack per setup. Recommend (a) for now.
9. **Presentation follows project focus.** With two projects open, clicking into B retargets every physical output and stream to B (`OutputSetupHandling.cs:29`). (a) Document it. (b) Pin
   presentation to an explicit live project. Recommend (a) now and (b) before two-project shows.
10. **`Prop` naming.** `Kind` never branches, and `Position` is Board metres though the docs say stage. Rename to `Person`, drop `Kind`, use `BoardPosition`. Recommend doing it while the format is cheap.

## Before release

These change the file format, so they are cheap now and need migrations later.

| Change | Finding or decision |
|---|---|
| Linked pins and fitted patches stop saving `Quad`; always write `AspectRatio`, `Scale` | 6 |
| `MappingModes.Linked` (optional) | 7 |
| A real `Setup.Version`, read and bumped | 7, decision 4 |
| `Surface.CanvasPatchId` | decision 2 |
| `Patch.IsBase` | decision 5 |
| `WallHeightOverrides` or no following | 17 |
| `ShowGrid` as session state (optional) | 9 |
| `Prop` to `Person` | decision 10 |
| Stable display key (machine file) | 11 |

## Answered: this is fine

No need to check these again. Details and line numbers are in the sections named.

**Inferred relationships (A)**
- Implicit slice: display and default only (`SetupRelations.cs:301-337`). Sharing goes through `CloneSliceForSurface` and `FindReusableSlice`.
- `PointAims` are keyed by id, and clone re-keys them (`SetupActions.cs:1229-1239`). Stream plugs use a real id.
- A copied op gets a new id and its own unrouted source (`CopySymbolChildrenCommand.cs:93`). That is correct.
- `TryFindSendParent` taking the first match is harmless while child ids are unique. Nested scope logic is sound (`ContentSourceSync.cs:47-71,380-405`).
- `ContentSource.Name`/`IsRenamed`: the op owns them, mirrored per frame (`:342-355`). Renaming a non-instantiated source is a silent no-op.
- `0.0005f` tolerances in `FloorPlanSync` are numeric, not identity. `ActiveSetupName` by name has a documented fallback.
- A patch link is a stored id, and the sync handles a deleted patch (`SetupRelations.cs:168-171`).
- Another machine has no bindings for these ids, so it doesn't guess.

**Two owners (B)**
- `Apply` and `FollowWallWidth` have a stable direction (`FloorPlanSync.cs:47,103-106`). Lowered surfaces are excluded (`Setup.cs:254`).
- `mapping.Quad` writers all promote or clear the link, except `PinCalibration.cs:192` (finding 7) and `SurfaceGeometry.cs:179` (finding 16).
- Resolution has a defined precedence: op input, then output or fitted patch, one pull per frame with a conflict warning (`SendToOutput.cs:37-48`, `OutputCompositor.cs:128-134`). Quads are normalized.
- Routing: `SendToOutput` holds none, and `TargetIds` no longer exists.
- Selection: one shared entity plane, and `ClaimInspection` keeps graph and setup exclusive (`GlobalSelectionHandling.cs:26,34-47`). `_shownSurfaceId` is frame-scoped.

**Meaning by context (C)**
- Coordinate spaces and Y flips are consistent (`RegionSlices.cs:101-106`, `SliceUvAnchoring.cs:50-54`, `StageGeometry.cs:302-330,383-402`, `SurfaceGeometry.cs:239-278`, `StagePlacing.cs:31`, `WallpaperLayout.cs:311-317`), except the anchor field (finding 8).
- `FloorPlan.BoardPlacement` is view only. Its doc is stale, the code isn't (`FloorPlanSync.cs:25-26`, `StageOrigin.cs:64-66`).
- `CalibrationPoint.OutputPixel` in pixels is inert: no editor UI writes it.
- One `Canvas` Guid with several meanings is acceptable, because ids don't collide. The fallback and the picker are wrong (finding 10).
- `ContentSource` keys: every site uses the right id. Only review protects this, since both are bare `Guid`s.

**Lifecycle and undo (D, E)**
- Macro order is safe in `RegionSlices`, `WallpaperLayout` and `ContentSourceSync`.
- Undo holds no object references apart from `_marks`, and lists are replaced rather than mutated, so a `foreach` higher up survives.
- The "new" snapshot taken before the sync is harmless, because `Apply` ends in `SaveActive`.
- `Apply` not running repair is correct. The cure is edit-time validation (finding 4).
- JSON comparison and fitted quads: a spurious step needs a display change during a motionless gesture, and a real edit is never hidden.
- Writes outside undo are all derived: source sync, Board seeding, image size, plugs, per-frame sync and fit.
- Static state: `_deleteBuffer`, `_reorderOldJson`, `_pendingMove`, `PendingPlanDrawId`, `CalibrationOverlay` are safe or cosmetic with two windows and two projects.
- `Trace.Annotations` not re-keyed: nothing reads those ids.

**Iteration and parity (F, G)**
- Alt+drag on slices, patches, regions and cards is safe (`Board.cs:156`, `Patches.cs:43,202`, `Regions.cs:337,401`, `Board.cs:284,1414-1419`).
- `RemoveVertex` in the corner loop has its `break` (`Board.cs:1114`). Outliner rows use index loops. The delete key runs before the columns.
- The compositor and resolver depend only on Core, and the editor hook is `ICalibrationOverlay`, null in the Player.
- Walls, floor plans and region quads render from persisted data and Core code in the Player.
- `RevealActiveFile` is guarded for non-Windows.

## Frame order

Editor (`WindowsUiContentDrawer.cs`):

1. `:153` `ImGui.NewFrame`
2. `:157` `OutputSetupHandling.UpdateFrame`: publish, resolve resolutions and re-fit, sync pins, notify only on a switch
3. `:158` `ContentSourceSync.UpdateFrame`: adopt sources, debounced save
4. `:163` `OutputPresentation.UpdatePresentation`: composite and hand off each bound output
5. `:165` blit output windows
6. `:212` `T3Ui.ProcessFrame`, per window: `DrawFrame`, `DrawBoardLayer`, `HandleBoardDrag`, `ResolvePicking`, outliner, `ApplyPendingMove`. Keyboard undo runs here, outside the list loops.
7. `:220` render, Present

Player (`Program.RenderLoop.cs`): playback and audio, `OutputFrame.Advance` (`:127`), `DrawOutputWindows` (`:98`), main composite (`:101`), `SendStreams` (`:136`), Present. Fit, sync and repair do not run per frame.

Staleness: a drag in frame N reaches displays in N+1, and a linked pin follows its patch one frame later. Both are constant and invisible. Only the `StageGeometry` case in finding 6 is visible.

Deferred statics that can lose their target are re-resolved safely, except those listed in findings 13 and 17 and under Fragility. `PendingScaleToolImageId` clears only when that image draws, so undoing a delete later opens the tool, which is trivial.

## Phase 2: Lifecycle table

**Legend:** DANGLE: id left pointing at nothing. COPY→ORIG: copy still points at the original. LEFT: children left behind. LOSS: data not restored. ·: nothing points at it.

**Applies to every row:** undo and redo restore the whole snapshot in place, with the same ids. Setup rename touches only the Setup row. Op hot reload touches only ContentSource and is safe. A setup switch keeps ids in a duplicate and uses fresh ids in a new setup, and selection prunes by id.

| Entity | Delete | Duplicate | Undo of each | Op deleted in graph | Project reopen | Output removed | Display unplugged | Other machine |
|---|---|---|---|---|---|---|---|---|
| **Surface** | Subtree removed, plans released. Slices stay: LEFT | Fresh ids, unfed, pins nudged. Own-pin children: COPY→ORIG | Fine | `SliceId` cleared, not undone: LOSS | Repair (finding 4) | Mapping dropped | Pin unaffected | Same |
| **Region** | As surface. Slice named after it stays: LEFT | Own pin on top and linked: COPY→ORIG | Fine | As surface | No parent or cycle check, unreachable from UI | Own pin dropped, rides parent | · | · |
| **Mapping/pin** | With surface. Deleted point leaves an aim key: LEFT, inert | Keys re-keyed; aims and backup not nudged | Fine | · | Re-link by value each load | Removed, undo restores | Canvas may change, pin doesn't | Same, unless finding 4 fires |
| **Patch** | Linked `PatchId` DANGLE for one pass, then cleared; quad kept | Fresh id, same slice, no pin follows | Link restored | `SliceId` DANGLE, never healed | Duplicate-id repair; re-link target | Gone with output | Fitted quad re-fit and saved | Fitted quad re-fit, shared file changes |
| **Output** | Mappings removed; binding removed, also for other setups. Op references DANGLE, then fallback | Fresh ids, no mappings or binding; patches keep feeds | Back unbound: LOSS | · | Resolved per frame | n/a | Binding stays, "not attached", 1920×1080 if following | Unbound; by index if machine file copied |
| **Slice** | Surfaces and patches cleared | Fresh id, no consumer | Fine | Removed, not undone: LOSS | UV healed on read | · | · | · |
| **ContentSource** | Via op: macro with snapshot, order safe | Not allowed | Macro restores | Sweep; new `Id` after undo: LOSS | Sweep on first frame | · | · | Possible sweep |
| **ReferenceImage** | `Trace` nulled; `Output.ReferenceImageId` DANGLE, harmless | Fresh id, same Board place, no traces | Fine | · | Size refreshed outside undo | · | · | Missing asset logs once |
| **Prop** | Nothing points at it | Fresh id, same place | Fine | · | · | · | · | Falls back on image |
| **FloorPlan** | Surfaces stay as placed, by design | Slots cleared; `Lowered`/`WallElevations` inert | Fine | · | Slots re-trimmed | · | · | · |
| **Annotation** | Aim key LEFT | Re-keyed; trace copies keep ids, inert | Objects replaced | · | Regenerated only if id missing | · | · | · |
| **Setup** | File deleted, no undo: LOSS; steps no-op | New `Id`, same entity ids | Not undoable | · | By `ActiveSetupName`, else first file | · | · | First file if name differs |
| **PlugBinding** | Orphan harmless; outside undo | Shared across duplicates | Never restored | · | Read before the setup | Removed | Stays, by index only | Absent, or wrong screen by index |

## Phase 3: Scenarios

| # | Scenario | Result |
|---|---|---|
| 1 | Trace, regions, slices, map to patches, edit plan, re-trace, update | Holds in the data. Regions keep metres; a linked parent remaps them (16); regions past a new edge get a clamped UV (17); re-meter paths without follow lead to 8; keystoned extension can trigger 4 at reload. |
| 2 | Duplicate with regions, slices, links; undo twice; redo once | Undo is clean with exact ids. Slices aren't copied. Root links end, region own-pin links stay (15). Redo restores the copy without selecting it. |
| 3 | Delete linked patch, output, region, send; undo each | Patch: link returns. Output: returns unbound (12). Region: clean. Send from setup UI: clean. **Send from graph: routing lost, patches dangling (1).** |
| 4 | Rename everything | Output, plan, image and stream: label only, links by id. Patch or surface: stored pins unaffected, name matches and `IsInUse` flip (10). Region: its slice keeps the old name until Update overwrites it (17). Labels can lag up to one second. |
| 5 | Copy a send, Combine, open inside | Copy: correct, new unrouted item. **Combine: routing lost, no undo (1).** Copying the combined op: one source for several instances (17). Opening inside: scope unchanged unless the new symbol has composition settings. |
| 6 | Other machine, other displays, no NDI | Without the machine file: unbound, 1920×1080, no crash. With it copied: by-index mismatch (11), and the "not attached" row **crashes** (2). A missing NDI package warns once and keeps the binding. The first edit writes this machine's fit (6). Nothing enforces "meant to be gitignored". |
| 7 | Switch setups with gesture, menu, rename; two windows | Mouse gestures can't reach the menu; the gamepad can (13). An open menu closes and ids re-resolve. A rename commits against the old setup, correctly. The rest of the frame draws the old object after `TrySwitchTo`. Outputs flash as they rebuild. |
| 8 | Player export: regions, patch links, plan, `ProjectOntoStage` | Regions and floor plan: fine. Stretched links: fine. Fitted links: stale (6). `ProjectOntoStage`: probably no shader (9). Raster left on: visible (9). |
| 9 | Non-finite pin, minimum region, save and reload | Non-finite: a corner silently at (0,0) (4). Far-out pin: reset and overwritten (4). Region at `MinSize`: fine. |
| 10 | Empty setup, `use-case-flows.md` | See below. |

**Scenario 10, flow by flow.** The flows doc is out of date in flows 1 and 2.

| Flow | Result |
|---|---|
| 1 | Works with different steps. No auto-connect or binding dropdown. Drag the send onto a display plug, which creates, routes and binds, and goes fullscreen at once. |
| 2 | Works by implicit patch corners. "+ Surface" is unmapped and unfed, not auto-mapped. No arrow-key nudge. |
| 3 | Core works. Drop needs the Board. No autostart option; a saved binding resumes. |
| 4 | Duplicate works. No readiness panel. Output ids share one binding (12). |
| 5 | Dead-ends after the second mapping: no test pattern, identify, soft edge, warp, colour or fixtures. |
| 6 | Camera steps don't exist. Manual points, aim and solver work. |
| 7, 8 | Not built. |
| 9 | Works up to routing. Identify, the point of the flow, is missing. |

## Not covered

- Nothing was run. Worth one short session: finding 2's three crashes, finding 1's Combine, finding 9's export, and the display order suspicion in finding 11.
- `.gitignore` coverage of the machine file was grep-checked only.
- Not verified: whether Duplicate Symbol preserves child ids; what happens to `ReferenceImage.FilePath` when an asset moves; whether `ReconnectChildren` instantiates a new send before its composition opens; every field of the parameter cards inside undo (a sample was walked).
- Cross-platform touchpoints still to move: `Screen.AllScreens` (`Plugs.cs:50,60,141`, `SetupEntities.cs:246`, `SetupFlowOutliner.cs:583`, `SetupParameterView.cs:987`, `DisplayLayoutView.cs:111`, `OutputWindowHandling.cs:150`), `ms-settings:display` (`DisplayLayoutView.cs:88`), and WinForms in `Program.OutputWindows.cs`. Setup name validation differs per OS.

## Phase 1: Model map

Classes: **ID** identity, **LINK** stored link, **SoT** source of truth, **DERIVED** computable but stored, **MACHINE**
machine-specific, **VIEW** editor presentation stored in the shared file.

### Setup (`Core/Output/Setup.cs`), one file per venue at `.meta/Setups/<Name>.setup.json`

| Field | Class | Keeps it valid / notes |
|---|---|---|
| `Id` | ID | Regenerated by `Duplicate`. Guards `SetupSnapshotCommand` so undo can't cross venues. |
| `Name` | SoT **and** DERIVED | Also the file name (`SetupFiles.FilePathFor`). A hand-renamed file disagrees with the JSON; `SaveActive` then writes by `Setup.Name`. |
| `Surfaces`, `ContentSources`, `Slices`, `Outputs`, `Props`, `ReferenceImages`, `FloorPlans` | SoT | Replaced wholesale by undo (list and element identity change). |
| Version | constant `1` | Never incremented across format changes. Unavailable as a "run once" gate today. |

### Surface (`Core/Output/Surface.cs:239-323`)

| Field | Class | Keeps it valid / notes |
|---|---|---|
| `Id` | ID | Fresh in `CloneSurface`; kept by `Setup.Duplicate`. A missing id reads as `Guid.Empty`; repair doesn't check surfaces. |
| `Name` | SoT, and used as a **link** | `SetupRelations.TryFindCanvasQuad`, `StageGeometry` name lookup, `FloorPlanSync.IsInUse`. |
| `Type` | SoT | Only `Rect` exists. |
| `Kind` | SoT | Physical or Layout. Repair detaches a nested Physical. |
| `ParentId` | LINK | Delete removes the subtree. Repair doesn't detect a dangling parent or a cycle. |
| `LocalPosition` | SoT | Metres from the parent's anchor, Y up. |
| `IsRendered` | SoT | |
| `SliceId` | LINK | Cleared by `DeleteSliceInternal`. No load-time check. |
| `SizeInMeters` | SoT **and** DERIVED for walls | Width follows the floor plan segment; two writers (see B). |
| `Anchor` | SoT | Surface-local origin. |
| `IsAspectLocked`, `ShowGrid`, `GridSubdivisions`, `PixelsPerMeter` | SoT | |
| `Annotations` | SoT | Metres. Ids are keys of `PointAims`. |
| `OutputMappings[]` | SoT | See below. |
| `Trace` | SoT | See below. |
| `Placement.Pose` | SoT **and** DERIVED for walls | Written by `FloorPlanSync`. |
| `BoardPlacement` | VIEW, with one leak | Presentation, except that Wallpaper orders unplanned walls by it (finding 17). |

### OutputMapping (`Surface.cs:37-172`)

| Field | Class | Keeps it valid / notes |
|---|---|---|
| `OutputId` | LINK | Delete-output removes the mapping. No load-time check. |
| `Mode` | SoT | `Fill` or `CornerPin`. Repair rewrites a fill's quad. |
| `Quad` | SoT when unlinked; DERIVED when `PatchId` set or `Fill` | Canvas 0..1, Y down. Overwritten by `SyncPinsToLinkedPatches` every editor frame and on save. **Not synced in the Player.** |
| `PatchId` | LINK | Dangling cleared by the per-frame sync. Cleared by `PromoteToCornerPin`/`FillCanvas`. Re-created by `LinkPinsSavedWithoutPatchIds` by comparing values, on every load. |
| `PointAims` | LINK (key) + SoT (position) | Keyed by annotation id. Deleting a point leaves an inert key. |
| `QuadBeforeAligning`, `ModeBeforeAligning` | SoT, backup copy | Not carried by space changes and doesn't cover the link (finding 7). |

### TraceBinding (`Surface.cs:175-211`)

| Field | Class | Keeps it valid / notes |
|---|---|---|
| `ImageId` | LINK | Delete-image nulls `Trace`. |
| `Quad` | SoT | Reference image pixels, Y down. |
| `PatchId` | **Dead field** | Doc copied from the mapping. Neither written nor read by the JSON. |
| `Annotations` | SoT | Photo pixels; different owner space from `Surface.Annotations`. |

### OutputDefinition (`Core/Output/OutputDefinition.cs:260-320`)

| Field | Class | Keeps it valid / notes |
|---|---|---|
| `Id` | ID | `DefaultOutputId` is shared across setups on purpose. |
| `Name` | SoT, optional | Empty means labelled from the binding via `SetupLabels.OutputLabel`. |
| `Kind` | SoT | `Default`, `Format`, `Projector`, `Display`. The Default is hidden from reorder. |
| `CanvasResolution` | SoT | 0×0 follows the plug. Reader defaults a missing key to 1920×1080, the field to 0×0. |
| `ResolvedResolution` | DERIVED, MACHINE, runtime | Not serialized. Resolved each editor frame; once in the Player. |
| `Camera` | SoT | Manual look-at and solved pose/lens. `CalibrationPoint.OutputPixel` is in **pixels**, unlike every normalized quad. |
| `IsSending` | SoT, maybe MACHINE | In the shared file. |
| `Patches[]` | SoT | See below. |
| `BoardPlacement` | VIEW | |
| `ReferenceImageId` | LINK | **Not cleared** when the image is deleted (`SetupActions.cs:952`). |
| `ReferenceOpacity` | SoT | |

### Patch (`OutputDefinition.cs:126-258`)

| Field | Class | Keeps it valid / notes |
|---|---|---|
| `Id` | ID | Duplicate-id repair on load. |
| `Name` | SoT, used as a **link** and as a **role flag** | Empty contributes to "implicit patch". Match target for surfaces by name. |
| `SliceId` | LINK | Cleared by delete slice. |
| `Quad` | SoT if stretched; DERIVED if fitted | Fitted quads are rewritten from the canvas each frame **and saved**, so a machine's display aspect reaches the shared file. |
| `QuarterTurns` | SoT | Also drives implicit role. |
| `ScaleMode`, `AspectRatio`, `Scale` | SoT | Written only when fitted, so toggling loses them across a snapshot. |

### Slice, ContentSource, ReferenceImage, Prop, Annotation, FloorPlan

| Entity.Field | Class | Keeps it valid / notes |
|---|---|---|
| `Slice.Id` | ID | |
| `Slice.SourceId` | LINK | `RemoveContentSource` cascades. No load-time check. |
| `Slice.Name` | SoT, role flag | Empty contributes to "implicit slice". **Update Slices from Regions** overwrites it. |
| `Slice.UvRect`, `Rotation` | SoT | Healed on read. The full-frame value is also a role flag. |
| `ContentSource.Id` | ID | Distinct from the outliner key, which is `SymbolChildId` (`FindSourceByChildId`). |
| `ContentSource.SymbolChildId` | LINK into the graph | Not unique across compositions or instances. Maintained by `ContentSourceSync`. |
| `ContentSource.Name`, `IsRenamed` | DERIVED | Mirrors the op; written by `ContentSourceSync` and at creation in `RegionSlices`/`WallpaperLayout`. |
| `ContentSource.BoardPlacement` | VIEW | |
| `ReferenceImage.Id` | ID | |
| `ReferenceImage.FilePath` | LINK by address | Asset address; broken by asset moves: check. |
| `ReferenceImage.Width/Height` | DERIVED | Cached from the file. |
| `ReferenceImage.MetersPerPixel` | DERIVED from scale line | Plus `ScaleLine*`, which are SoT. |
| `ReferenceImage.IsLocked`, `Opacity`, `BoardPlacement` | VIEW | |
| `Prop.*` | SoT | `Kind` always `Person`. Name-less. No board placement of its own. |
| `Annotation.Id` | ID | Regenerated on read when empty, so an id-less file gets new ids per load. |
| `FloorPlan.Id`, `Name`, `Vertices`, `IsClosed`, `WallHeight` | SoT | |
| `FloorPlan.FloorSurfaceId`, `WallSurfaceIds[]`, `LoweredSurfaceIds` | LINK | Released by `FloorPlanSync.ReleaseSurfaces`. Duplicate clears slots but keeps `Lowered`, `WallElevations` (`SetupActions.cs:1134`). |
| `FloorPlan.WallElevations` | SoT keyed by LINK | |
| `FloorPlan.FloorRect` | SoT, optional | Null means bounding box. |
| `FloorPlan.BoardPlacement` | VIEW | `FloorPlan.cs:15` says it places the plan in the stage, but `FloorPlanSync.Apply` uses origin zero and `StageGeometry` ignores it. The doc is stale. |

### Machine file (`outputs.machine.json`, `Core/Output/MachineConfig.cs`)

| Field | Class | Notes |
|---|---|---|
| `Bindings[].OutputId` | LINK | Across files, so outside undo. Same ids across duplicated setups. |
| `Bindings[].Kind`, `PlugId` | MACHINE | |
| `Bindings[].DisplayIndex` | MACHINE | **Alone decides the display.** |
| `Bindings[].DisplayName` | MACHINE | Recorded, never resolved. The class summary still says "matched by name first". |
| `ActiveSetupName` | MACHINE | By name, so renames on other machines fall back. |
| `StreamPlugs[]` | MACHINE | |

### Relationship graph

Stored:

```
ContentSource --SymbolChildId--> graph child (live, any symbol)
Slice --SourceId--> ContentSource
Surface --SliceId--> Slice                     Patch --SliceId--> Slice
Surface --ParentId--> Surface
Surface.OutputMapping --OutputId--> Output
Surface.OutputMapping --PatchId--> Patch  (must belong to OutputId)
Surface.OutputMapping.PointAims[key] --> Annotation.Id
Surface.Trace --ImageId--> ReferenceImage      Output --ReferenceImageId--> ReferenceImage
FloorPlan --WallSurfaceIds[], FloorSurfaceId, Lowered, WallElevations--> Surface
PlugBinding --OutputId--> Output (other file)  PlugBinding --PlugId--> StreamPlug
```

Computed (each is a candidate for section A):

```
Surface ~~same Name~~> Patch                   TryFindCanvasQuad, StageGeometry, IsInUse
Pin <~~equal Quad~~ Patch with no slice        LinkPinsSavedWithoutPatchIds
Patch "implicit" <~~ index 0, no name, full canvas, no turns, not fitted
Slice "implicit" <~~ first of source, no name, full UV, no rotation
ContentSource <~~ op found by scanning symbols (TryFindSendParent)
Region <~~> Slice                              only through region.SliceId
Display <~~ current position in arrangement    DisplayIndex
```

### Against `data-model.md`

- It lists `Surface.OutputMapping.SourceQuad`, `ReferenceBinding`, `DeviceBinding`, `StagePlacement.Pivot`, a Setup at
  `.meta/<name>.setup.json` and `OutputDefinition.SliceId`. None of these exist as described.
- `TargetIds`-era text and the "Region owns only its content, never a mapping" rule (§2.3) are out of date: regions can carry their own pins.

### Against `selection.md` and `states.md`

- `selection.md` matches the code on one shared entity selection (`GlobalSelectionHandling.cs:26`). It differs on the sub-element plane: it says shared (`:37`), but `_canvasSelection` is per view (`SetupOutputView.cs:953`).
- `states.md` already marks its gutter section as dead. It wasn't compared item by item with the outliner's drawing.
