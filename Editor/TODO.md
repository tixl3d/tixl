## Important issues

- [x] Fix camera handling
- [x] Default gradients are not loaded?
- [x] Fix Scaling for multiple selected keyframes with ALT-Key
- [x] Fix Gradient editor not working as parameter window parameter
- [x] Deleting last output will cause crash
- [x] Bypassed operator are active after reload / bypassing again breaks their update
- [x] Modifications by BiasGain Vec2 gizmo can't be undone
- [x] Fix thumbnails rendering breaks defaults
- [?] Connections from input are sometimes not correctly evaluated 
- [x] Picking video files from resource does not work.

- [x] In Parameter window bypassable button should be disabled if not available
- [x] Prevent Keyboard Camera interaction while input field is active 
- [x] Exit should ask before quit
- [ ] Rearranging parameters with additional annotations (e.g. ShaderParameters) breaks operator 
- [ ] Pre/Post Curve modes are applied to all (not just selected curves)
- [ ] Indicate Pre/Post curve moves in timeline
- [ ] Ask before removing inputs and outputs (can't be undone)
- [ ] SequenceAnimUi should be visible before evaluation (Check AnimValue)

## Project handling

- [x] Reduce size of backups
- [x] Allow to override Project location
- [x] Make sure that TiXL is using the high performance GPU
- [x] Prevent creating projects with existing names
- [ ] unload projects from project list
- [ ] Load last project from user settings

## Graph

- [x] Allow dragging connections from vertical output slot
- [x] Dragging gradient widget handles drags canvas too
- [x] Snapping connecting start to output not working of ops who's output is already snapped
- [x] Add annotations
- [x] Parameter window in fullscreen
- [x] Fix split between multi input parameters
- [x] Fix background control in mag graph
- [x] Allow dragging connection from horizontal input slot
- [x] Allow clicking vertical input slot
- [ ] Publish as input does not create connection
- [ ] Split Connections
- [ ] Rewiring of vertical connection lines
- [ ] LoadImage has no thumbnail
- [ ] Panning/Zooming in CurveEdit-Popup opened from SampleCurveOp is broken 

- [ ] Add hint message to hold shift for keeping connections
- 
## Timeline

- [ ] Soundtrack image is incorrectly scaled with playback?
- [ ] After deleting and restart recompilation of image is triggered, but image in timeline is not updated?
      Path not found: '/pixtur.still.Gheo/soundtrack/DARKrebooted-v1.0.mp3' (Resolved to '').

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

- [ ] Shake doesn't work with 120hz

## Ops

- [x] Remove Time 2nd output
- [ ] Rename Time2 <-> Time
- [ ] Rounded Rect should have blend parameter
- [ ] Fix BoxGradient
- [ ] SetEnvironment should automatically insert textureToCubemap
- [ ] Remove Symbol from Editor
- [ ] Fix SnapToPoints
- [ ] Sort out obsolete pixtur examples
- [ ] Rename PlayVideo to LoadVideo
- [ ] Add RotateImage or add option to [TransformImage]

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
 


## Refactoring
- [ ] Remove ICanvas
- [ ] Refactor to use Scopes

## Long-Term ideas:
- [ ] Render-Settings should be a connection type, including texture sampling, culling, z-depth