# StreamDiffusionExample

*in [Lib.io.ai](README.md)*

Generates an image from a prompt with SD Turbo. Point ModelPath at an ONNX export folder containing unet.onnx, text_encoder.onnx, vae_decoder.onnx and tokenizer files; AutoGenerate redraws when the prompt changes.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **ModelPath** (String) | Folder with the SD Turbo ONNX export. |
| **Prompt** (String) | Text prompt. |

## Outputs

| Name | Type |
|---|---|
| **Image** | T3.Core.DataTypes.Texture2D |


