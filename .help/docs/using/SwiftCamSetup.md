# Swift Imaging cameras

The `SwiftCamDevice` operator captures live video from a [Swift Imaging](https://swiftimaging.com/) scientific USB camera and exposes it as a TiXL `Texture2D`. It uses the vendor SDK directly (not DirectShow), which gives you hardware ROI, manual exposure, and color delivery at the camera's native resolution.

This page covers a one-time setup: where to put the SDK's `swiftcam.dll` so TiXL can find it, plus how to verify the camera is working.

## What you need

- A Swift Imaging camera connected over USB 3.x (USB 2.0 may enumerate but the SDK can refuse to stream).
- The Swift Imaging SDK, version 3.0 or later. The vendor distributes this with the camera or as a separate download.
- TiXL running with the `Lib` operator package available.

## Install `swiftcam.dll`

`swiftcam.dll` is the vendor's native camera driver. TiXL is not allowed to redistribute it (the vendor's SDK ships no redistribution license), so you copy it once into a per-user folder.

1. From the Swift Imaging SDK, locate `swiftcam.dll` under `win/x64/`. **Use the x64 build, not x86** — TiXL is a 64-bit process and will reject the x86 version with a `BadImageFormatException`.
2. Create the folder `%LOCALAPPDATA%\TiXL\NativeDeps\` if it doesn't exist. In Explorer that's typically `C:\Users\<you>\AppData\Local\TiXL\NativeDeps\`.
3. Copy `swiftcam.dll` into that folder. The expected path is:
   ```
   C:\Users\<you>\AppData\Local\TiXL\NativeDeps\swiftcam.dll
   ```
4. Connect the camera. The vendor's USB driver (`swiftcam.inf` / `swiftcam.cat` from the SDK) should already be installed; if Windows shows the camera as an unknown device in Device Manager, install the driver from the SDK first.
5. Restart TiXL (or trigger **Reconnect** on an existing `SwiftCamDevice` instance).

If you'd rather keep the DLL elsewhere, set the environment variable `TIXL_SWIFTCAM_DLL` to its full path. The operator checks that variable first.

## Verify the install

1. Drop a `SwiftCamDevice` operator into a composition.
2. Open the **Device Name** dropdown. You should see your camera listed by its product name (for example `Swiftcam_SC1003`).
3. Pick the camera and toggle **Active** on. The **Status** output should change to `Streaming.`.
4. Wire **Texture** into a viewer — `Layer 2 d`, `Display`, or any operator that renders a `Texture2D`. Live video appears.

If the dropdown shows `swiftcam.dll not found`, the DLL isn't where the operator is looking. Re-check the path from step 3 above, then trigger **Reconnect**.

If the dropdown shows `No Swift cameras found`, the SDK is loading but no camera is detected. Check Device Manager — the camera should appear under its own category and not as `Unknown USB Device`. Replug the cable, or run the vendor's Swift Imaging app to confirm the OS sees the camera; close the vendor app before retrying TiXL (most camera SDKs only allow one process to hold the camera).

## Tuning the live stream

`SwiftCamDevice` ships with sensible defaults. Three knobs matter most for live performance:

**Resolution** — the dropdown lists every preset the camera reports, with actual pixel dimensions (e.g. `0: 3664x2748 (full)`, `1: 1832x1374`). Higher indices are typically binned/downsampled and run faster. Full-sensor 10 MP at 30 fps over USB 3.0 is **not possible** — the bandwidth is roughly 1.2 GB/s, beyond practical USB 3.0 throughput. For real-time preview, pick a lower resolution.

**Auto Exposure** — on by default, matching the camera's own default. Off lets the **Exposure** input take effect (in milliseconds). Lower exposure → faster maximum frame rate (1 / exposure_ms × 1000) → darker images.

**Region of Interest (ROI)** — set **Roi Resolution** to a non-zero `(W, H)` to crop the sensor in hardware. **Roi Alignment** (`-1..1` per axis) places the crop window: `(0, 0)` = centered, `(-1, -1)` = top-left, `(1, 1)` = bottom-right. Snapped to multiples of 2 pixels for Bayer-pattern alignment.

> **Hardware ROI is unreliable on the Swiftcam_SC1003 (firmware 4.0.5).** The SDK accepts `put_Roi` and reports `EVENT_ROI` confirming the change, but no frames ever flow afterwards — `EVENT_ERROR` fires ~1 second later regardless of the ROI size or alignment. This is a firmware / SDK bug we can't work around in software. **Use `Resolution Index` for downsampled modes instead, then crop downstream in TiXL** if you need a specific aspect ratio. Other Swift cameras (or a newer SC1003 firmware) may handle ROI correctly — the operator preserves the inputs in case yours does.

**Recommended workflow for live preview at 30+ fps:**

1. Set `Resolution Index = 1` (1832×1374, half-binned). Halves the data per frame.
2. Set `Auto Exposure = false` and `Exposure = 10` ms or less. Frame rate is capped at `1000 / exposure_ms`.
3. Leave `Roi Resolution = (0, 0)`.
4. If you need a tighter region (e.g. 1080p), wire a software-crop or framebuffer operator after the texture output — at this resolution the GPU cost is negligible.

This gives ~50 fps on the SC1003 over USB 3.0.

Auto-recovery is built in: a USB hiccup, the camera being unplugged, or the SDK reporting an error all trigger a clean automatic reconnect roughly every second until the camera comes back. The **Status** output and the operator's status badge surface the current state.

## Troubleshooting

**`swiftcam.dll not found`** — the file isn't at the expected path. Confirm with `dir %LOCALAPPDATA%\TiXL\NativeDeps\swiftcam.dll` from a command prompt. The file must be x64 (~12 MB).

**`Bad image format`** in the editor log — you copied the x86 build by mistake. Replace with x64.

**`Camera reported an error. Auto-retrying…`** — the SDK can occasionally fail to start streaming on a fresh `Open()`. The operator retries every second; usually it self-heals within a few attempts. If it persists, trigger **Reconnect**, or unplug and replug the camera physically.

**`Camera disconnected`** — physical USB drop, cable issue, or the vendor app stole the handle. Plug back in and the operator reconnects automatically.

**Camera stops responding and Reconnect doesn't fix it** — observed on the SC1003 after repeated `put_Roi` attempts: the camera firmware can enter a stuck state where neither `Active = off/on` nor `Reconnect` recovers it. **Physically unplug and replug the USB cable** to reset the device. The vendor's app exhibits the same behaviour, so this is firmware-side. Avoid configuring a non-zero `Roi Resolution` on the SC1003.

**Frame rate lower than expected** — check **Exposure** (manual) or scene brightness (auto). Long exposure caps frame rate. Then check the **Resolution** dropdown — full sensor at 10 MP is intrinsically bandwidth-limited.

**Verbose log output filling up** — the **LogMessages** input is on. Toggle it off; only errors and one-time lifecycle events log by default.

## What this operator doesn't do

By design, `SwiftCamDevice` is the raw input — one job, no extras. Downstream operators handle:

- White balance, tone mapping, gamma — apply a regular image-processing chain.
- Dark-frame subtraction, temporal accumulation, denoising — pending dedicated operators; for now compose with existing image filters.
- 16-bit / RAW Bayer output — pending; the operator currently delivers 8-bit BGRA after the SDK's internal demosaic.

## See also

- The [TiXL operator browser](../operators/index.md) to find image-processing operators that consume the texture.
- The vendor's product page for camera-specific specs (sensor size, exposure range, supported binning modes).
