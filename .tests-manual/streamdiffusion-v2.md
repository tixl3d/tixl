---
id: streamdiffusion-v2
title: StreamDiffusion V2 Video Restyling
scope: streamdiffusion
tags: [ai, video]
added: 2026-11-02
added-in-version: 4.4
prerequisites:
  - A scratch project, not one that is shared over a collaboration session.
  - An NVIDIA GPU with CUDA (or TensorRT with its runtime on PATH) and enough memory for the export (256x256 with 2-4 steps, or 512x512 with 1 step).
  - A StreamDiffusion V2 model folder from Operators/StreamDiffusion/tools/streamdiffusionv2.
  - An image or a live video texture for the input.
---

Verifies the StreamDiffusion V2 video pipeline: model loading, continuous restyling, motion adaptation and stream restarts.

## Step: Selecting the V2 pipeline

**Action:**
Add `[StreamDiffusion]`, set `ModelType` to "StreamDiffusion V2", point `EnginePath` at the exported model folder and set `Mode` to "Stream".

**Expected:**
- The operator's status starts as "Loading model..." and then reads "Ready (CUDA, img2img: yes)".
- The log names the loaded model folder. A folder that misses a required file reports exactly which file is missing.

## Step: Unsupported providers fail clearly

**Action:**
Set `ExecutionProvider` to DirectML with the V2 model still selected.

**Expected:**
- The status reports that StreamDiffusion V2 does not support DirectML and points to CUDA or TensorRT.
- No frames are generated, and the log contains the same reason. Switching back to CUDA recovers without a restart.

## Step: Restyling a live stream

**Action:**
Connect an image or video texture to `InputImage`, enter a prompt such as "a watercolor painting of a fox in a forest", and enable `AutoGenerate`.

**Expected:**
- `Output` shows the input continuously restyled by the prompt, not a single still.
- `OutputSize` matches the export (256x256 or 512x512) and `GenerationTime` reports a value per update.
- A float render target, for example through `[AdjustColors]`, is accepted too: no "Unsupported input format" warning appears, and colors look the same as an 8-bit image input.
- The first frames after selecting the pipeline show the encoded input until the stream has produced its first denoised frame; the image never goes black.

## Step: Motion adaptation

**Action:**
Keep the input moving (play a video or animate an image transform) and compare `StreamStrength` at 0.8 with a still input.

**Expected:**
- With fast motion the output follows the input more closely, with less prompt-driven change.
- With a still input the prompt takes over more strongly.
- Changing `StreamStrength` does not restart the stream.

## Step: Restarting the stream

**Action:**
Change `Seed`, then change `Steps` between 1 and 4.

**Expected:**
- Each change briefly re-primes the stream; the output may flash the encoded input for a frame or two and then continues with the new seed or step count.
- With `Steps` above 1 the same input frame advances several denoising stages in one batch, which raises GPU memory use.

## Step: Resolution and memory

**Action:**
With the 512x512 export, set `Steps` to 2; then switch back to a 256x256 export and set `Steps` to 4.

**Expected:**
- `Width` and `Height` are ignored; the output stays at the export's resolution.
- On a 16 GB GPU the 512x512 export at 2 steps becomes very slow (the KV cache fills the card), while 256x256 runs 4 steps. Lower `Steps` or use a smaller export to recover speed.
