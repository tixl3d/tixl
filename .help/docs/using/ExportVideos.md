# Exporting Videos and Image Sequences

## TL;DR
You can use TiXL to render your project as a video or image sequence:

- Select (or pin) an image operator like [RenderTarget] in the **Output Window**.
- Use the output window's resolution selector to set your output resolution.
- Open **Render To File** — the sliders icon in the output window's toolbar, or via the *main menu*.
- Pick a **Range**, **Codec**, and output path, then press **Render**.

The filename extension follows the codec automatically (`.mp4`, `.mov`, `.mkv`), so you don't have to set it by hand.

![Alt text](/images/01-export-as-video.gif)

## Settings

The **Render To File** window groups its settings into three sections (and a help button in the panel header links back to this page):

- **Source** — the **Range** (Custom / Loop / Soundtrack / Continuous), the **Scale** unit (bars, seconds, frames), **FPS**, the **Resolution Scale** multiplier, and the **Motion Blur** override.
- **Format & Quality** — render mode (video or image sequence), **Codec**, **Bitrate**, and audio export.
- **Output Target** — the folder, filename, and auto-increment options.

Every parameter has a small **(?)** marker next to it — hover it for a one-line explanation. The footer shows a one-line summary with a rough size and render-time estimate.

![Alt text](/images/03-quality-preview.gif)

## About FFmpeg

FFmpeg is the open-source engine that does the actual video encoding — it turns the frames TiXL renders into a finished `.mp4`, `.mov`, or `.mkv` file. It is the same technology that powers most video software.

**FFmpeg comes built into TiXL.** There is nothing to install, download, or configure, and TiXL never has to call out to a separate program on your computer — exporting just works out of the box. The version TiXL ships uses license-friendly components, so the videos you export are free to use in your own projects.

## Codecs

A **codec** is the recipe used to compress the video. TiXL offers several, because no single one is best for everything — some are made for sharing online, some for editing, some for live VJ playback.

Every codec can carry an **AAC** audio track when *Export Audio* is on. **H.264** and **HEVC** use your graphics card's **hardware encoder** (NVIDIA NVENC, Intel Quick Sync, or AMD AMF) when one is available, which is much faster; otherwise TiXL falls back to a slower software encoder. The small line under the **Codec** dropdown tells you which one your machine will use.

| Codec | File | Best for | Notes & limitations |
|---|---|---|---|
| **H.264** | `.mp4` | The safe, compatible default — plays everywhere | 8-bit; quality set by **Bitrate** |
| **HEVC (H.265)** | `.mp4` | Smaller files than H.264 at the same quality | Software encoding is slow; some players need the `hvc1` tag to preview |
| **ProRes** | `.mov` | Handing footage to an editor (Premiere/Resolve) | High quality, **large** files |
| **VP9** | `.mp4` | Efficient web delivery | Slow to encode |
| **AV1** | `.mp4` | The most efficient delivery codec | Slowest to encode |
| **FFV1** | `.mkv` | Archival / mastering | Visually lossless, **very large** |
| **HAP / HAP Alpha / HAP Q** | `.mov` | Realtime / VJ playback (Resolume, etc.) | Cheap for the GPU to play back; HAP has no transparency, HAP Alpha/Q add alpha / more quality |

### File sizes

- **H.264, HEVC, VP9, AV1** are controlled by the **Bitrate** slider — higher bitrate means better quality and a bigger file. The hint next to the slider turns the current bitrate into a plain quality label for your resolution and FPS. As a rule of thumb, around *0.1 bits per pixel per second* is a good quality for sharing.
- **ProRes** ignores the bitrate (it has its own) — expect files roughly 10× an H.264 of the same shot.
- **FFV1** is lossless, so files can be **several GB per minute** at HD.
- **HAP** has a predictable size — about half a byte per pixel per frame (twice that for HAP Alpha / HAP Q). The window shows an exact estimate.

> **Tip:** Hardware encoding (NVENC / Quick Sync / AMF) is the fast path and matters most for realtime *Continuous* capture. NVENC needs an NVIDIA driver version 570 or newer.

## Continuous Capture (open-ended recording)

When you don't know the length up front — a live set, a performance, an audio-reactive jam — set **Range** to **Continuous**. Pressing **Render** then starts recording immediately and keeps going until you press the capture control again, which finalizes the file. There is no fixed end time.

Two clock models are available under **Source**:

- **Realtime** grabs the live output as you perform, paced to the target FPS, and leaves playback under your control. This is the mode for live/VJ use. It captures **video only** for now (no audio), at the native output resolution. A fast hardware encoder is recommended — with a software codec the capture may drop or duplicate frames to keep pace, and the render window warns you.
- **Deterministic** advances time at the target FPS until you stop, exactly like a normal render but without a predetermined end. It is frame-perfect even when encoding is slower than realtime, and records audio.

While a continuous capture runs, the progress bar is replaced by a sweeping activity indicator showing the captured frame count and elapsed time. Press **Stop** (or the capture shortcut) to finalize.

## High-resolution rendering

Rendering is cheap compared to encoding, so you can trade a little time for a lot of quality. Set **Resolution Scale** above 1 to render **larger than the output window** — for example 2 turns a 1080p project into 4K. Thanks to TiXL's [smart resolution setup](https://www.youtube.com/watch?v=f9E7lwUXfBM) the requested resolution flows through the whole project, so gradients, text, and antialiasing all sharpen up. This is the way to produce 4K masters or high-resolution stills from an HD comp.

## Rendering with Motion Blur

[RenderWithMotionBlur] renders several slightly offset passes per frame and blends them, producing very high-quality motion blur. Because that is too expensive to do live, you keep its sample count low (or 0) while working and **override it only for the export** with the **Motion Blur** field in the Render To File window — so your interactive session stays smooth while the export gets the full quality.

A **Motion Blur** of `-1` means "don't override" (the default). Values above `0` require a [RenderWithMotionBlur] operator somewhere above your output.

[Watch the tutorial here](https://www.youtube.com/watch?v=WK4j0H-oM3g&t=626s)

## Saving Screenshots

The screenshot (camera) icon in the output window's toolbar saves the current frame as a PNG into your project's `Screenshots/` folder.

**Ctrl-click** the icon to start **continuous screenshot mode**: TiXL then saves a screenshot on a fixed interval and the icon pulses in the attention colour, brightest right after each capture. Click the icon again to stop. Continuous capture pauses while a video or image-sequence export is running and resumes once it finishes.

**Right-click** the icon for options: start/stop continuous capture, pick the interval (1 s … 10 min), and choose the file format. PNG is lossless; **JPG** produces much smaller files, which is handy when capturing long continuous sequences. The interval can also be set under *Settings → Interface → Output*.

## Technical Background

TiXL renders into a GPU texture (by default `R16G16B16A16_Float` — 16 bits per channel, high precision in the 0…1 range). Getting that onto disk as a video is more involved than it looks:

- Many file formats store pixels as Blue, Green, Red, Alpha — the bytes have to be reordered.
- Reading a texture back from the graphics card takes up to a few frames.
- Converting between pixel formats (16-bit float to 8-bit, etc.) is comparatively slow.
- Effects are often audio-reactive, but a **deterministic** export is not realtime — TiXL drives the timeline frame by frame so the result is exactly repeatable.
- Some operators are asynchronous (loading an image from the web, seeking a video). During a deterministic export TiXL waits for each to finish before writing the frame, trading speed for correctness.

These are the reasons a *Continuous → Realtime* capture (which grabs the live output instead) trades that determinism for keeping pace with a live performance.

## Roadmap

Current exports are 8-bit. HDR output (e.g. EXR image sequences) and variable-frame-rate continuous capture are on the roadmap.
