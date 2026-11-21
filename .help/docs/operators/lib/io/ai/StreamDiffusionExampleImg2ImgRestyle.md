# StreamDiffusionExampleImg2ImgRestyle

*in [Lib.io.ai](README.md)*

Transforms an image with a prompt. Strength controls how far the result moves away from the input: 0 keeps the image, 1 replaces it. The export needs vae_encoder.onnx for img2img.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **ModelPath** (String) | Folder with the SD Turbo ONNX export. |
| **ImagePath** (String) | Image to transform. |
| **Prompt** (String) | Text prompt. |

## Outputs

| Name | Type |
|---|---|
| **Image** | T3.Core.DataTypes.Texture2D |


