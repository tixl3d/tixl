# StreamDiffusion V2 export tools

Converts the [StreamDiffusionV2](https://github.com/chenfengxu714/StreamDiffusionV2) weights (a causal Wan2.1 1.3B video
DiT) into the ONNX folder the `StreamDiffusion` operator loads when `ModelType` is **StreamDiffusion V2**.

The PyTorch modules here are independent re-implementations of the model layout; the upstream repository is only a
reference for the architecture and is never imported or executed.

## Ready-made export (no Python needed)

The Model Browser carries a **"StreamDiffusion V2 - 256x256 (ready-to-run)"** entry that downloads the
finished ONNX export (DiT, text encoder, TAEHV, tokenizer) straight into the model store. Use it unless
you need a different resolution or want to customize the export.

## Model store flow

The Model Browser entry "StreamDiffusion V2 checkpoint (for export)" downloads `model.pt`,
`models_t5_umt5-xxl-enc-bf16.pth` and `tokenizer.json` into
`%LOCALAPPDATA%\TiXL\Models\StreamDiffusion\streamdiffusion-v2-checkpoint\`. Run the three
steps below against those files (pass their full paths) and write the finished export into its
own folder next to the checkpoint, for example
`%LOCALAPPDATA%\TiXL\Models\StreamDiffusion\wan-480p\`. The operator scans that folder when
`EnginePath` is empty, and the setup check turns green once the export files are in place.
`taew2_1.pth` comes from the taehv GitHub project and is not part of the download.

## Inputs

| File | Source |
|---|---|
| `model.pt` (causal generator) | `jerryfeng/StreamDiffusionV2`, `wan_causal_dmd_v2v/model.pt` |
| `models_t5_umt5-xxl-enc-bf16.pth`, `google/umt5-xxl/tokenizer.json` | `Wan-AI/Wan2.1-T2V-1.3B` |
| `taew2_1.pth` | `https://github.com/madebyollin/taehv` |

## Steps

Run from this folder, then copy everything into one model folder together with `tokenizer.json`.

```bash
python export-dit.py --ckpt model.pt --out out --height 480 --width 832 --verify
python export-text-encoder.py --t5 models_t5_umt5-xxl-enc-bf16.pth --out out --verify
python export-vae.py --taehv taew2_1.pth --out out --verify
```

Resulting folder: `dit_step.onnx(+.data)`, `dit_manifest.json`, `cross_kv.onnx(+.data)`, `kv_zeros.onnx`,
`text_encoder.onnx(+.data)`, `vae_encoder.onnx`, `vae_decoder.onnx`, `tokenizer.json`.

The resolution is baked into `dit_step.onnx`; export once per size (multiples of 16). Memory use grows with the token
count: the GPU-resident KV cache is `layers x denoising steps x 6 frames x tokens` fp16 pairs, plus the same again while a
step runs.

Measured on a 16 GB RTX 3080 Ti Laptop GPU, fp16, TAEHV, after warm-up, with ONNX Runtime 1.30, CUDA 13.3 and
TensorRT 10.16 (the operator's own stage timers):

| Export | Provider | Steps | Decode window | Steady state | Notes |
|---|---|---|---|---|---|
| 256x256 (`tokensPerFrame` 256) | TensorRT | 1 | 1 (`FastDecode`) | **~0.12-0.14 s/frame (~8 fps)** | fastest; encode ~10 ms, DiT ~100 ms, decode ~10 ms |
| 256x256 | TensorRT | 1 | 3 | ~0.17-0.19 s/frame | decode ~11-26 ms, better temporal smoothing |
| 256x256 | CUDA | 1 | 1 (`FastDecode`) | ~0.25-0.27 s/frame | encode ~35 ms, DiT ~190 ms, decode ~28 ms |
| 256x256 | CUDA | 1 | 3 | ~0.31-0.35 s/frame | encode ~35 ms, DiT ~205 ms, decode ~70-105 ms |
| 256x256 | CUDA | 2 | 3 | ~0.33-0.43 s/frame | twice the temporal context, 2x KV cache |
| 256x256 | CUDA | 4 | 3 | ~2.8 s/frame | the 4x KV cache plus the in-flight copy leaves little VRAM headroom on 16 GB |
| 512x512 (`tokensPerFrame` 1024) | CUDA | 1 | 3 | ~3.3-5 s/frame | decode dominates, not realtime on a 16 GB laptop |
| 512x512 | CUDA | 2 | 3 | fills the 16 GB card | throughput collapses; not practical |

`FastDecode` is the single cheapest speed-up: it decodes one latent instead of a three-latent window, so the decode
stage drops to ~10 ms on TensorRT and ~28 ms on CUDA, at the cost of temporal smoothing. It is an operator input and
restarts the stream when toggled.

The step count is the batch size, so every extra step doubles the KV cache. Attention cost grows with the square of
`cacheFrames * tokensPerFrame`, so 512x512 is roughly 16x more expensive per step than 256x256 at the same batch size.
Larger GPUs can run more steps at 512; below 16 GB, stay at 256 for multi-step streaming.

The RTX 3080 Ti Laptop throttles at 87°C under sustained streaming (clocks oscillate between 1.1 and 1.8 GHz), so long
takes show occasional 2-3x slower frames. On this machine with the fp16 TensorRT engines the steady state is
denoise ~55-85 ms with periodic 400-900 ms dips every 1-2 s once the GPU heat-soaks; lower **TargetFps** (8-10) to
keep clocks high and the dips away - stability costs a little average throughput. It is a cooling limit, not a leak: GPU memory stayed flat at ~7.5 GB over a
100-frame soak and returned to idle after disposal. The operator skips identical input frames, so a frozen source stops
using the GPU entirely; prompt changes encode in the background (the stream keeps the previous prompt) and the encoded
prompt is cached on disk, so a known prompt starts in about 1.4 s instead of loading the 11 GB text encoder (~40-60 s).

## Execution providers

| Provider | Status | Notes |
|---|---|---|
| CUDA | verified | Fastest warm path; used for all measurements above. Needs the CUDA 13 runtime and cuDNN 9 (ONNX Runtime 1.30, driver 580+); CUDA 13 no longer supports Pascal GPUs, which fall back to CPU |
| TensorRT | verified | Builds fp16 engines on first use (about 12 min for all five graphs, 2.8 GB in `trt_cache`); later runs reuse them. The CUDA provider uses HEURISTIC cuDNN search, so shape changes no longer trigger exhaustive autotune stalls. The DiT gets an explicit 1-4 batch profile, so Steps can change without a rebuild. TensorRT runs alone here: adding CUDA as a per-node fallback made the DiT roughly 6x slower in testing (7 min vs 1 min 12 s for the same frames) |
| DirectML | not available | ONNX Runtime stopped publishing DirectML builds after 1.24.4, and its DML provider fails the DiT and VAE 5D reshapes (`node_view_*`) with an internal error anyway. Selecting it logs that the runtime ships no DirectML provider and continues on CUDA or CPU |
| CPU | works, very slow | Only practical for a one-off test; the 1.3B DiT step takes minutes |

TiXL finds the TensorRT 10 runtime itself - the standard `NVIDIA GPU Computing Toolkit\TensorRT-*\bin` install,
`TENSORRT_ROOT`, or a `TensorRTRuntime` folder next to the editor - and preloads `nvinfer_10.dll` and the ONNX parser
before ONNX Runtime asks for the provider, so `PATH` does not need to change. If no runtime is found, the operator falls
back to CUDA with a readable reason.

TensorRT builds those engines on the first run of each graph, and the builder needs transient workspace on top of the
resident sessions; when the card cannot supply it the build fails (TensorRT reports its workspace cap as
`OutOfMemory`). The operator reports that failure once and re-initializes the model on CUDA, so the stream recovers
instead of repeating the error every frame. The cap defaults to 4096 MB and can be overridden with
`TIXL_SD_TRT_WORKSPACE_MB` (set it lower on cards with little free memory). The provider cache lives in `trt_cache` next to the model files and can be deleted
to force a rebuild.

The provider statuses above are from Windows. Linux uses the same CUDA/TensorRT path (the CUDA 13 requirements apply,
so Pascal GPUs fall back to CPU); macOS (arm64) builds and falls back from CoreML to CPU, but has not been verified at
runtime - the CoreML provider does not support every exported graph shape.

## Design notes

- The reference mutates per-layer KV caches in place with data-dependent control flow. Here the DiT step is stateless:
  cache tensors, a per-slot visibility mask and the destination slot go in, the updated caches come out. The C# pipeline
  owns the slot policy (3 sink frames, 3 rotating frames).
- The T5 encoder keeps bf16 weights and computes in fp32: fp16 overflows to NaN in this model.
- TAEHV replaces the Wan 3D VAE for both encode and decode; it runs over a short window of frames per step instead of
  carrying state.

## Known differences from the reference

- Adaptive sink refresh (scene-cut detection) and the RoPE rewind for streams longer than about 1000 latent frames are not
  implemented; the pipeline re-primes instead.
- The first two latent frames are denoised one after another rather than as one two-frame block.
