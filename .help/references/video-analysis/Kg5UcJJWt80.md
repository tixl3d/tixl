---
video: Kg5UcJJWt80
type: meetup
date: 2026-06-15
title: Snapshots & presets, FFmpeg video/audio/data clips, 360 optimization, ArtNet, Skill-Quest
duration: 4:45:18
---

A long TiXL community meet-up centered on a reworked snapshot/preset system that exposes individual parameters (rather than whole operators) to blendable snapshots, demonstrated by building VJ-style scenes live. Also covers the new FFmpeg-based video, audio, and MIDI/OSC "data clip" recording pipeline, the guided-feature-test window, an equirectangular 360-projection performance optimization, ArtNet/DMX light control, and authoring skill-quest tutorial levels.

## Mentions
- 0:55 [DrawLineText] [DrawPoints] · in-depth — opening live doodle: line-text points converted to a point buffer
- 1:20 [PointsToGpuBuffer] · passing — converting line-text points to a GPU buffer
- 1:30 [SubdividePoints] · explained — subdividing the line points before adding noise
- 1:43 [AddNoise] · explained — animating noise on the points with a phase input
- 6:03 [DrawPoints] · in-depth — picking size/color params to expose to per-parameter snapshots
- 13:30 [SnapshotIndex] · explained — driving snapshot cycling via an int animation into the index
- 15:25 [ChromaticDistortion] · explained — chromatic-distortion post effect toggled per snapshot index
- 15:35 [Glow] · passing — switching style effects between snapshot variations
- 16:00 [PickTexture] · explained — index-based selection of which post effect applies
- 20:55 [FastBlur] · passing — example output op for live render previews in the snapshot view
- 25:35 [PointsOnMesh] · in-depth — building a real demo: emit points on a posed mesh
- 25:40 [TransformPoints] · explained — translating selected points away from the center
- 31:40 [TSNE] · explained — t-SNE dimensionality reduction idea for laying out presets in 2D
- 40:55 [BlendSnapshots] · explained — existing blend-snapshots op and its awkward index API
- 50:00 [PointsOnMesh] · in-depth — emitting points on the posed mesh
- 50:15 [DrawLines] · explained — combining two line sets to draw an eagle-like shape
- 50:40 [OrbitCamera] · explained — orbit camera added to the demo scene
- 50:53 [Bloom] · explained — "we always want bloom" added to the scene
- 51:30 [SelectField] · explained — selecting points by SDF distance
- 51:50 [PlaneSDF] · explained — plane SDF used as the selection field
- 52:00 [RemapValue] · explained — ping-pong mapping on the selection range
- 52:55 [OscillateValue] · explained — oscillate value driving sphere movement
- 53:20 [SphereSDF] · explained — moving sphere SDF to drive a point selection
- 56:10 [PerlinNoise] · explained — Perlin noise added to camera rotation offset for shake
- 57:00 [OrbitCamera] · in-depth — camera-shake snapshots built on orbit camera
- 1:06:45 [Gradient] · explained — blendable gradients must share the same number of steps
- 1:08:32 [DepthOfField] · explained — DoF distance made snapshot-controllable
- 1:08:40 [OrbitCamera] · in-depth — orbit-camera target distance exposed to snapshots
- 1:22:30 [OrbitCamera] · explained — preset vs. snapshot distinction explained via orbit camera "close-up"
- 1:50:00 [VideoClip] · in-depth — new FFmpeg-based video editing intro
- 1:53:00 [PlayVideo] · explained — old Media-Foundation play-video and its one-frame seek delay
- 1:55:40 [VideoClip] · in-depth — new VideoClip player op, auto-freeing of unused players
- 1:57:50 [VideoClip] · explained — cross-fading/blending two video clips by timeline order
- 2:00:25 [AutoCollectTimeClips] · explained — auto-collect feature gathering all clips on a layer
- 2:23:55 [AudioClip] · in-depth — dragging MP3 to create an audio clip with auto-generated spectrum
- 2:26:40 [MidiInput] · in-depth — recording MIDI/OSC and audio simultaneously (data clips)
- 2:27:40 [Layer2D] · explained — media input into a Layer2D scale demo
- 2:28:20 [RemapValue] · explained — remapping rotation output range to -180..180
- 2:30:00 [SimulateIoData] · in-depth — recorded clip played back via SimulateIoData into a group
- 2:32:20 [TeachTrigger] · explained — teach-trigger picking up simulated MIDI events
- 2:41:00 [MediaInput] · in-depth — MediaInput range output as a float list to texture
- 2:41:20 [ConvertToTexture] · explained — visualizing the float list as a texture
- 2:42:00 [AnalyzePianoMidi] · in-depth — new analyze-piano-MIDI op outputting pulse/last-normalized-note
- 3:08:20 [CustomSDF] · in-depth — custom SDF raymarch field for the 360 demo
- 3:08:55 [OrbitCamera] · explained — orbit camera flying through the SDF field
- 3:13:40 [SliceViewport] · in-depth — slice-viewport op to limit rendered area for performance
- 3:13:50 [EquirectangularCamera] · explained — equirectangular/cube-map camera for 360 projection
- 3:14:00 [CubeMap] · explained — scene drawn six times into a cube map
- 3:18:35 [CubeMap] · explained — disabling/enabling top & bottom cube faces to save renders
- 3:19:45 [LoadImage] · explained — feeding a black texture in place of unused cube faces
- 3:20:20 [Black] · explained — built-in Black texture op (cache-resource) instead of disconnecting
- 3:21:35 [Crop] · explained — cropping the equirect output to drop unused ceiling/floor pixels
- 3:26:16 [DrawBillboards] · in-depth — switching to DrawBillboards with point orientation
- 3:27:05 [OrientPoints] · explained — orient-points op to face camera / look-at-target
- 3:30:08 [DrawRailLines] · in-depth — DrawRailLines used for seamless 360 line rendering
- 3:37:55 [Duplicate] · explained — Ctrl+Shift+D duplicate-with-connections (smart reconnect)
- 3:55:40 [Instancing] · in-depth — building the "Instancing" skill-quest level with two transforms
- 3:58:50 [Transform] · in-depth — two transform ops (left/right eyes) connected to a group for instancing
- 4:04:30 [Transform] · explained — "Transform Order" level: order of move/rotate/draw matters
- 4:16:50 [PointsToArtnetLights] · in-depth — points-to-ArtNet-lights op for DMX control
- 4:24:45 [VisualizePoints] · explained — visualize-points showing position/orientation/color
- 4:28:00 [PointsToFloatList] · explained — grid-layout float list mapping color to DMX channels
- 4:29:00 [Merge] · explained — merge op assigning position/color into the universe
- 4:30:10 [LinearGradient] · passing — gradient defining a section of the DMX universe
- 4:31:20 [OrientPoints] · explained — orienting light points to "point down"/aim precisely
