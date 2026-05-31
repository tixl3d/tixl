# TiXL v4.2 - Release Notes (WIP)

## UI Features


### New Timeline

We've completely rewritten TiXL's timeline window.
- **Timeline Ruler** is now easier to use at the top of the area and no longer interferes with the Win11 taskbar
- It features a new **Selection Range** Area to quickly rescale or even time warp animations.
- Below is a new **Selection Set** area to quickly select or move all keyframes at a time.
- The **Inline Curve Editor** is now integrated and can be opened for each parameter of the dopesheet by clicking the curve icon.

### New Animation Curve

We've completely reimplemented TiXL's animation curve system (Nerd speech: We switched the interpolation from *Monotone Hermite Bézier* to *Cubic Bezier*). This allows us to stretch tangents (hold Ctrl+Shift) and define much more expressive animation curves.

On the way we've also cleaned up the names of the keyframe interpolation types.

Sadly, this is a breaking change. That means that although older TiXL versions can still open these projects, some of the new animation data will be lost.

### Audio Clips

You can drag audio files directly into the timeline and edit them like time clips. They behave slightly differently, though: they will NOT be visible in the graph and don't need to be connected for playback.

### Recording IO Tracks

You can now record live sets as io-data- and audio-clips. io-Clips can then be replayed, with simulated MIDI and OSC events sent to the existing operators. That's a great method to prepare for a live show!

### Settings are Project specific

Many settings are now saved directly with your project:
- Output Resolution, Loop Range, Layout, Pinned operators, etc.

### Skill Quest Tips

The skill quest tutorials are awesome. But sometimes hunting down the precise float parameter value can be frustrating. TiXL 4.2 comes with an improved tutorial guidance system that will point out if unnecssary parameter have been modified and nudges you what to try next.

### Markdown Format in the Editor

Most documentation and description texts are now displayed with Markdown formatting, including links. References like [Layer2d] will directly link to the Symbol Library.

### New Alpha-Version Workflow

Testing "alpha" versions is crucial for development. But it shouldn't interfer with production work: TiXL now clearly separates directories used by preview versions. To help with this, you'll be greeted with the new "Welcome to Alpha" window that gives an overview of the new features, how to test them and helps importing your projects.

### Guided Testing

TiXL now ships with a suite of guided test sets that cover many of TiXL's editor features. Interactive features are notoriously hard to test automatically. Although these tests were initially planned to test for regressions, the guided tests turned out to be great for walking users through new features and discovering all the details.

## New Operators

- [SwiftCamDevice] access to Swift microscope cameras
- [ReadPointColors] a simple helper to read colors from points
- [WLedSerialOutput] Sends LED color data to WLED.

## Examples
- [MoveMeshToPointLineExample] description missing