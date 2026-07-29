# Long Term Features related to output handling

Review once in a while to prioritize

When connecting a new display or projector, if have to stop presenting and represent.

## Next major steps

**1. Finish the canvas toolkit first: crop → sub-surfaces → duplicate → snap**

This is the biggest gap between what exists and the workflow *you already wrote*. Steps 10–14 of your straighten use case (crop the extent, add sub-regions, duplicate, snap) are all still missing, and the slice we just landed stops right where they'd begin.

More importantly it's the **substrate everything else reuses**: annotation-line handles, slice rects, mask outlines and warp lattice points are all draggable canvas points with snapping. Building measurement or masking first means building them on an unfinished editing layer, then reworking. `CanvasPointHandle`/`CornerPinHandles` already prove the pattern — extend it once, benefit four times.

**2. Slices — and note they now have an obvious home**

Worth calling out a synergy from what we just built: **the Content end of the morph is exactly the canvas where slice editing belongs.** You straighten onto a surface, keep pushing to Content, and you're looking at the source with the surface's crop — the natural place to drag a slice rect. `SendToOutput.SourceRect` already exists as a `Vector4` UV, so this is largely UI, not new plumbing. Sub-surfaces and slices are the two halves of "split one render across many targets," so they pair naturally right after (1).

**3. Annotation-line straighten + dimensions from lengths**

These two are one feature, not two — draw lines on physical features, solve the rectification, type a real length, derive size/aspect. They're already the `Straighten` / `Apply Lengths` buttons in your toolbar sketch, and they turn the current *eyeball the grid* into something rigorous. The data is half there (`ReferenceBinding.Annotations`, `LineAnnotation`, `ProjectorSolver`), but it's currently bound to reference *images* — the work is making annotations live on the output canvas too. High value, and it's the honest completion of the straighten story.

**4. Masks and warp — genuinely later**

Both are already sketched in the data-model plan as `OutputMapping` growth (`Warp?`, `Mask?`, `CornerColors[4]?`), deliberately deferred. They're **new rendering capability** (shader work in `corner-pin-layer.hlsl` + pipeline changes), not workflow completion — nothing else is blocked on them. Masks earn their keep the moment you have a hard-split wall or soft-edge overlap; warp only once you hit a surface that isn't actually planar. I'd let a real need pull them in rather than build them speculatively.





### General

- Add a **point annotation** kind next to line annotations (position + label, string-discriminated,
  same `CanvasPointHandle` + undo skeleton as line endpoints). Independently useful for manual survey
  points/measurement anchors now; the enabler for camera-assisted calibration later (see
  `camera-calibration.md` §4.1).
- Regression: selection a [SendToOutput] op no longer selects the item
- Selecting an operator should open the settings side panel
- probably replace / retire Screen manager
- Implement narrow Vec2 to support optional linked icon toggle





## For later:

- Setup menu should provide options to rename setup
- In straight mode selecting different surfaces should have a view transition 
- Setup menu should have a hover indication (e.g. change font color ForeGroundFull)
- Unused surfaces (without any Content being routed into should be shown as unused)
- it would be nice to have a vertical splitter to adjust the width of the output settings panel
- Deleting an operator on the graph should also delete the Content item in the side panel
- Collapsed-States of items and sections should be saved with the Setup.
- We need an option to remove a reference like Content -> Surface. Not sure, about the interaction though. Some ideas:
  1. Context menu on Content Item listing all targets
  1. Listing the inputs in the target (e.g. Surface)
  1. Selecting a surface icon could show input target indicators (e.g. Icon.ArrorLeft) on the left side of potential targets. Active targets are highlighted (e.g. with UiColors.StatusActive). Inactive would be show as BackgroundFull.Fade(0.5). Clicking the input indicator would toggle the reference.
  1. Dragging a source onto a target could clear the old reference (Maybe this could be avoid with a keyboard shortcut)
- It would be great to have a quick fix "rotate/pan" project mapping (e.g. somebody hit the project tripod would primarily affect the rotation of projector, so rotating and panning all corner points of a surface would be nice).
- Surface Context menu could have menu item "Rename Surface" that focuses (and scrolls into view if necessary) the surface name input
- Rearranging / ordering the nested surface structure via drag and drop
- Default name of sub-regions should be `{ParentSurfaceName}.{IncrementedCounter}`
- Fix gizmo initialization with output setup rendering and selecting `ITransformGizmoProvider` in graph



## More use-cases for surfaces

- A surface could have axis aligned guide lines (relative to anchor points). E.g. image to create a rectified surface mapping to the front facade of a house. Then change to straightened mode. 



## Use-case: "blinking boards"

1. Create gradient
2. (Gradient + IntAnim)->Steps->SendToOutput
3. Connect projecter
4. 







## Visual Cleanup:

- Narrow Form Inputs
  - Default input size -> 23
  - New ShortName combine op
  - Align paddings and high between label and input
  - Add number formatting suffix (e.g. m or px)
  - 

