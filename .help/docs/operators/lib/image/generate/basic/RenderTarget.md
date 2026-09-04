# RenderTarget

*in [Lib.image.generate.basic](README.md)*

The primary method of rendering 3D data into a 2D image texture. You will need this for all post-processing image effects and many other applications.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Command** (Command Required) | — |
| **Resolution** (Int2) | Overrides the default resolution or the resolution of the Output window.<br/><br/>If the resolution is:<br/>- [0,0], the current resolution requested by the Output window is used.<br/>- For image fx operators, negative resolutions will use the incoming resolution.<br/><br/>All other settings use custom resolutions. <br/>The maximum resolution depends on your graphics hardware and is normally 16k.<br/> |
| **Clear** (Boolean) | — |
| **ClearColor** (Vector4) | Defines the RGBA value of the world background.<br/><br/>This setting can be transparent to composite the rendered image over another background. |
| **Multisampling** (Int32) | Selects the quality level of the Multisample anti-aliasing (MSAA). <br/><br/>Higher values create smoother and less pixelated results but can have a high performance impact on slow GPUs (Graphics Cards).<br/><br/>For details see: https://en.wikipedia.org/wiki/Multisample_anti-aliasing |
| **TextureFormat** (Format) | — |
| **WithDepthBuffer** (Boolean) | You will need a depth buffer for all Z-Buffer sorting.<br/>Combining a depth buffer with MultiSampling requires internal downsampling: This basically means doubling the GPU memory consumption. |
| **WithNormalBuffer** (Boolean) | — |
| **GenerateMips** (Boolean) | — |
| **TextureReference** (RenderTargetReference) | In combination with [UseRenderTarget] this can be used for feedback effects. |
| **EnableUpdate** (Boolean) | Enables / Disables rendering. This can be very useful when connected to [Once] for caching the rendering after an initial update. |

## Outputs
| Name | Type |
|---|---|
| **ColorBuffer** | T3.Core.DataTypes.Texture2D |
| **DepthBuffer** | T3.Core.DataTypes.Texture2D |
| **NormalBuffer** | T3.Core.DataTypes.Texture2D |

