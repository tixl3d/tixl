## Important issues

- [?] Connections from input are sometimes not correctly evaluated 
- [ ] Rearranging parameters with additional annotations (e.g. ShaderParameters) breaks operator 
- [ ] Pre/Post Curve modes are applied to all (not just selected curves)
- [ ] Indicate Pre/Post curve moves in timeline
- [ ] Ask before removing inputs and outputs (can't be undone)
- [ ] Fix MultiInput connection editing
- [x] Indicated HDR colors
- [ ] Combine into new Symbol should prefill current project and namespace
- [ ] Command bar shortcuts should work if UI is hidden
- [ ] Maybe bookmarks should toggle pinning?
- [ ] Inserting keyframes does not always use neighbour smoothing
- [ ] Fix fade graph when background is interactive 
- [ ] Looks like only last animated value edit to a vec3 can be undone?
- [ ] Remove clear background image when clicking on left edge
- [ ] Rethink bookmarks -> Add marker in Op with number / switch with numbers only. only bring to view if hidden
- [ ] Export should use project folder and some prefix like _

next:
- [ ] Don't show hidden input popup on snap!!!


# UI

## Feedback from UncleX

- [ ] Graph context menu should have an option to Add (and show keyboard shortcut)
- [ ] Maybe add option to insert ops via Esc
- [ ] Fix "don't disconnect unsnap"
- [ ] Drag and split vertical connection lines
- [ ] Output-Nodes should have a show in Output indicator
- [ ] Tooltip + short for pinning in Output window bar
- [ ] Create [HowToUseVariables]

## Project handling / Project HUB

- [ ] Project settings should save output resolution
- [ ] Project hub context menu Open in Explore is not working #719
- [ ] Load last project from user settings
- [ ] Scrolling in project hub list #716
- [ ] unload projects from project list
- [ ] Project backups should be project specific
- 

## Graph

- [ ] Publish as input does not create connection
- 
- [ ] Split Connections on drop
- [ ] Rewiring of vertical connection lines
- [ ] LoadImage has no thumbnail
- [ ] Panning/Zooming in CurveEdit-Popup opened from SampleCurveOp is broken 
- [ ] Create connections from dragging out
- [ ] Add hint message to hold shift for keeping connections
- [ ] Raymarch UV spaces are not working
- [ ] FractalSDF -> SDFToColor -> FieldToImage (with color mode) is not working
- [ ] RandomizePoints HSB broken
- [ ] Add Field support for DrawMeshAtPoints 
- [ ] Ui Tweaks: Hide TabGroup close
- [ ] Refactor IStatusMessageProvider "Success" indication #714
- [ ] Rethink bookmarks

## Timeline

- [x] Implement delete clips
- [x] Soundtrack image is incorrectly scaled with playback?
- [x] After deleting and restart recompilation of image is triggered, but image in timeline is not updated?
      Path not found: '/pixtur.still.Gheo/soundtrack/DARKrebooted-v1.0.mp3' (Resolved to '').
- [x] Allow Dragging up/down with right mouse-button
- [ ] Add option to squeeze Layers area 

## UI-Scaling Issues (at x1.5):

- [x] Perlin-Noise graph cut off
- [ ] Timeline-Clips too narrow
- [ ] Full-Screen cuts of timeline ruler units
- [ ] MagGraph-Labels too small
- [ ] Panning Canvas offset is scaled
- [ ] Pressing F12 twice does not restore the layout
- [ ] Snapping is too fine
- [ ] in Duplicate Symbol description field is too small

- [ ] Add some kind of FIT button to show all or selected operators 

## High frame-rate issues 120Hz
- [x] Shake doesn't work with 120hz

## Ops

- [x] Remove Time 2nd output
- [x] Rename Time2 <-> Time
- [ ] Rounded Rect should have blend parameter
- [x] Fix BoxGradient
- [x] SetEnvironment should automatically insert textureToCubemap
- [ ] Remove Symbol from Editor
- [ ] Fix SnapToPoints
- [ ] Sort out obsolete pixtur examples
- [?] Rename PlayVideo to LoadVideo
- [ ] Add [OrientImage] with flip, rotate 90d, 180d 270d
- [ ] Clean up [SnapPointsToGrid] with amount
- [ ] FIX: Filter returns a point with count 0 (with random-seed not applied)
- [ ] Deprecate DrawPoints2
- [ ] Fix [RandomizePointsColor] !
- [ ] Cleanup *-template.hlsl -> -gs.hlsl
- [ ] [Set-] and [BlendSnapshots] (see API mock examples)
- 
- 
    
### Particles
- [ ] Provide optional reference to points in [GetParticleComponents]
- 


## SDF-Stuff

- [ ] Changing the parameter order in the parameter window will break inputs with [GraphParam] attribute
- [ ] Ray marching glow
- [ ] Some for of parameter freezing
- [ ] Combine flood fill with 3d
- [ ] FieldToImage
- [ ] Flexible shader injection (e.g. DrawMesh normals, etc.)
- [ ] ShaderGraphNode should be bypassable
- [ ] Undo/Redo seems to be broken when editing custom SDF shaders

## Documentation

- [x] Fix WIKI export does not include input descriptions

## General UX-ideas:

- [ ] Add mono-space font for code fragments
- [ ] StatusProvideIcon should support non-warning indicator
- [ ] Separate Value Clamping for lower and upper values 
- [ ] Drag and drop of files (copy them to resources folder and create LoadXYZ instance...)
- [ ] With Tapping and Beat-Lock, no Idle-Animation should probably "pause" all playback?
 
## Other features

- [ ] EXR image sequence support #740

## Refactoring
- [ ] Remove ICanvas
- [ ] Refactor to use Scopes

## Long-Term ideas:
- [ ] Render-Settings should be a connection type, including texture sampling, culling, z-depth