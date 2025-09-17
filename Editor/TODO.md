## Important issues

- [?] Connections from input are sometimes not correctly evaluated 
- [ ] Rearranging parameters with additional annotations (e.g. ShaderParameters) breaks operator 
- [ ] Pre/Post Curve modes are applied to all (not just selected curves)
- [ ] Indicate Pre/Post curve moves in timeline

- [ ] Ask before removing inputs and outputs (can't be undone)
- [ ] Fix MultiInput connection editing
- [ ] Combine into new Symbol should prefill current project and namespace
- [ ] Command bar shortcuts should work if UI is hidden
- [ ] Inserting keyframes does not always use neighbour interpolation type
- [ ] Looks like only last animated value edit to a vec3 can't be undone?
- [ ] Maybe bookmarks should toggle pinning?
- [ ] Rethink bookmarks -> Add marker in Op with number / switch with numbers only. only bring to view if hidden
- [ ] Export should use project folder and some prefix like _
- [ ] Fix tiny node-text with 200% display-scaling
- [ ] Focus selected op in SymbolLibrary
- [ ] Collapse Symbol Library

# UI
- [ ] add color preview to vec4 (and maybe a history gradient?)
- [ ] Create [HowToUseVariables]
- [ ] Scaling color to zero clears hue and saturation.

## Project handling / Project HUB

- [ ] Project settings should save output resolution
- [ ] Project hub context menu Open in Explore is not working #719
- [ ] Load last project from user settings
- [ ] unload projects from project list
- [ ] Project backups should be project specific

## Graph

- [ ] Publish as input does not create connection
- [ ] Split Connections on drop
- [ ] Panning/Zooming in CurveEdit-Popup opened from SampleCurveOp is broken 
- [ ] Create connections from dragging out of parameter window
- [ ] Refactor IStatusMessageProvider "Success" indication #714

## UI-Scaling Issues (at x1.5):

- [ ] Full-Screen cuts off timeline ruler units
- [ ] Pressing F12 twice does not restore the layout
- [ ] in Duplicate Symbol description field is too small
- [ ] Add some kind of FIT button to show all or selected operators 

## Ops

- [ ] Rounded Rect should have blend parameter
- [ ] Remove Symbol from Editor
- [ ] Fix SnapToPoints
- [ ] Sort out obsolete pixtur examples
- [?] Rename PlayVideo to LoadVideo
- [ ] Add [OrientImage] with flip, rotate 90d, 180d 270d
- [ ] Clean up [SnapPointsToGrid] with amount
- [ ] FIX: Filter returns a point with count 0 (with random-seed not applied)
- [ ] Deprecate DrawPoints2
- [ ] Cleanup *-template.hlsl -> -gs.hlsl
- [ ] [Set-] and [BlendSnapshots] (see API mock examples)
    
### Particles
- [ ] Provide optional reference to points in [GetParticleComponents]
- 
## SDF-Stuff

- [ ] Changing the parameter order in the parameter window will break inputs with [GraphParam] attribute
- [ ] Ray marching glow
- [ ] Some form of parameter freezing
- [ ] Flexible shader injection (e.g. DrawMesh normals, etc.)
- [ ] ShaderGraphNode should be bypassable
- [ ] Undo/Redo seems to be broken when editing custom SDF shaders

## General UX-ideas:

- [ ] StatusProvideIcon should support non-warning indicator
- [ ] Drag and drop of files (copy them to resources folder and create LoadXYZ instance...)
- [ ] With Tapping and Beat-Lock, no Idle-Animation should probably "pause" all playback?
 
## Other features

- [ ] EXR image sequence support #740

## Refactoring
- [ ] Refactor to use Scopes

## Long-Term ideas:
- [ ] Render-Settings should be a connection type, including texture sampling, culling, z-depth