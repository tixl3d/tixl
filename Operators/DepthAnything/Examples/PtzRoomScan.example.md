# PTZ Room Scan Example

Scans a whole room into one world-space point cloud with a pan/tilt (PTZ) network camera. The camera is driven
through a stop-and-capture raster; the pose of every capture comes from the camera's own pan/tilt readout, so the
cloud cannot drift and a full sweep closes around the camera.

**Ready-made composition:** `Examples.Lib.io.video.depthanything.PtzRoomScanExample` contains the whole chain below,
preconfigured for an AXIS P5635-E. Set **Address** to your camera and leave **Run** on: the sweep starts by itself and the
output draws the cloud as it fills in. Orbit the output view to look at the room from outside.

Tested with an AXIS P5635-E (360 degrees pan, 180 degrees tilt) over ONVIF + VAPIX. The camera cannot look below its
horizon, so a sweep covers 360 degrees of pan by 0 to 90 degrees up.

A camera that only rotates sees one surface per direction, so DepthScan3D keeps one range per 0.25 degree direction
(each capture's depth scale and shift are fitted against what is already merged, depth edges and single sightings are
dropped). Turn **RegularizeRoom** on to snap walls, floor and ceiling to a fitted box; it only suits rectangular rooms.
**FlatSurfaces** (on by default) finds the planes in the scan (walls, floor, ceiling, tables, any orientation, no box and no
right angles assumed) and moves the surfaces that lie on them onto flat planes; clutter that fits no plane keeps its shape.
**GlobalAlign** (off by default, experimental) solves every capture's depth scale jointly from all the places the captures overlap.
Save a closed triangle mesh with **SaveFormat** "PLY mesh".

## Composition Structure

```
[OnvifCamera] --Texture--> [DepthAnything] --DepthTexture / OutputTexture--> [DepthScan3D] --> [DrawPoints]
     ^                                                                          ^      ^
     | Pan / Tilt / Move                                         CaptureOnce ---+      |
[PtzScanPath] <--CurrentPan / CurrentTilt-- [AxisPtzCamera] --Camera--> CameraReference
```

- **OnvifCamera** moves the camera (absolute ONVIF moves) and delivers the video.
- **AxisPtzCamera** reads the true pan/tilt over VAPIX (30 Hz) and turns it into the capture camera, delayed by
  `DelayMs` so it matches the video frame the depth was computed from.
- **PtzScanPath** walks the stops: move, wait until AxisPtzCamera reports arrival, wait `SettleSec`, pulse
  `Capture` for one frame.
- **DepthScan3D** merges each capture into the voxel grid with the PTZ camera's pose.

OnvifCamera's Texture must feed DepthAnything: an operator only runs when something pulls its outputs, and the
video connection is what keeps OnvifCamera sending the moves.

## Node Configuration

### OnvifCamera
- **Address** `10.0.0.111`, **Username** / **Password**, **Connect** on

### AxisPtzCamera
- **Address** / **User** / **Password**: same camera
- **FieldOfView** 38.4: vertical field of view at the widest zoom (data sheet 63.5 x 38.4 degrees, 4.3 mm; fitted
  from image motion 61.8 x 37.2). **AspectRatio** 1.778
- Zoom: the field of view follows the camera's zoom readout through a magnification curve measured on the P5635-E
  (VAPIX zoom 500 = 2.44x, 1000 = 3.92x, 2500 = 8.78x, 9999 = 30x optical). Wire **VerticalFov** into
  DepthAnything's **FieldOfView** so the depth intrinsics follow the zoom too.
- **InvertPan** on, **InvertTilt** off (fitted against the camera's measured image motion)
- **DelayMs** 120: 80 ms measured stream latency plus depth latency. Raise it if moving captures smear.
- **ReferencePan / ReferenceTilt**: the view that becomes straight ahead and upright in the scan

### PtzScanPath
- **PanRange** 360, **PanStep** 40 (about 2/3 of the 62 degree horizontal view)
- **TiltMin** 0, **TiltMax** 90, **TiltStep** 25; **PoleTilt** 90 (the tilt that looks along the pan axis, where rows
  need only one stop). Tilt beyond 90 sees the same directions again with pan + 180, so 0..90 covers the half-sphere.
- **SettleSec** 0.8: video delay plus DepthAnything's temporal Smoothing
- ONVIF mapping for AXIS: **PanDegreesPerUnit** 180, **TiltDegreesPerUnit** 90, **TiltZeroAt** -1
- **Run** starts or pauses the scan, **Restart** returns to the first stop

### DepthAnything
- **CameraMotion**: irrelevant here (the scan uses AxisPtzCamera's pose, not CamReference)
- 16:9 export (`da3-small-392x224-fp16.onnx`) with **InputResolution** 392 for undistorted depth

### DepthScan3D
- **Capture** off (PtzScanPath pulses **CaptureOnce**), **Reset** before a new scan
- **VoxelSize** 0.02, **Mode** Average

## Expected Results

- About 30 captures for the default raster (fewer stops on rows near the pole), a few seconds each.
- A half-sphere of the room around the camera; overlapping captures average in the voxel grid.
- Monocular depth is relative: walls at different distances keep their shape, but the overall scale can differ
  slightly between captures.
