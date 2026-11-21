# StreamDiffusionExampleV2Restyle

*in [Lib.io.ai](README.md)*

Streams a restyled image with the StreamDiffusion V2 video model. AdjustColors feeds a float render target, which the operator converts on the worker; use CUDA or TensorRT and point ModelPath at the V2 export folder.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **ImagePath** (String) | Image to restyle. |
| **Prompt** (String) | Text prompt. |
| **ModelPath** (String) | Folder with the StreamDiffusion V2 ONNX export. |

## Outputs

| Name | Type |
|---|---|
| **Image** | T3.Core.DataTypes.Texture2D |


