# Lib.io.ai

## Operators

- [**StreamDiffusion**](StreamDiffusion.md) — AI image generation using Stable Diffusion with ONNX Runtime. Supports text-to-image, image-to-image and continuous video streams.
- [**StreamDiffusionExample**](StreamDiffusionExample.md) — Generates an image from a prompt with SD Turbo. Point ModelPath at an ONNX export folder containing unet.onnx, text_encoder.onnx, vae_decoder.onnx and tokenizer files; AutoGenerate redraws when the prompt changes.
- [**StreamDiffusionExampleImg2ImgRestyle**](StreamDiffusionExampleImg2ImgRestyle.md) — Transforms an image with a prompt. Strength controls how far the result moves away from the input: 0 keeps the image, 1 replaces it. The export needs vae_encoder.onnx for img2img.
- [**StreamDiffusionExampleV2Restyle**](StreamDiffusionExampleV2Restyle.md) — Streams a restyled image with the StreamDiffusion V2 video model. AdjustColors feeds a float render target, which the operator converts on the worker; use CUDA or TensorRT and point ModelPath at the V2 export folder.

---

*Auto-generated from the operator library.*
