# CustomPixelShader

*in [Lib.image.use](README.md)*

Creates a custom shader from a source parameter. This can be useful for prototyping.

Use "Ignore Template" to bring in your entire shader.


## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ImageA** (Texture2D) | — |
| **ImageB** (Texture2D) | — |
| **Offset** (Vector2) | — |
| **A** (Single) | — |
| **B** (Single) | — |
| **C** (Single) | — |
| **D** (Single) | — |
| **GainAndBias** (Vector2) | — |
| **Gradient** (Gradient) | — |
| **ShaderCode** (String) | — |
| **AdditionalCode** (String) | — |
| **TemplateFile** (String) | — |
| **Resolution** (Int2) | — |
| **TextureFormat** (Format) | — |
| **GenerateMips** (Boolean) | — |
| **Clear** (Boolean) | Clearing RenderTarget<br/>setting it to false also enables blending mode, so that result is blended on top of backbuffer |
| **ShaderResources** (ShaderResourceView) | Use SrvFromTexutre2d for additional texture inputs. They will need to be defined in AdditionalCode, for example:<br/><br/>Texture2D<float4> Image3 : register(t2); |
| **ConstantBuffers** (Buffer) | Use FloatsToBuffer or Ints to buffer to pass additional input params, define in AdditionalCode for example:<br/><br/>cbuffer AdditionalIntParams : register(b2)<br/>{<br/>    int Index;<br/>}<br/>cbuffer AdditionalFloatParams : register(b3)<br/>{<br/>    float4 Color;<br/>} |
| **CustomSampler** (SamplerState) | Use SamplerState node to add custom sampler(s); first sampler input is defined in template as CustomSampler.<br/>samplers coming after that will need to be defined in AdditionalCode, for example:<br/><br/>sampler Sampler2 : register(s2); |

## Outputs
| Name | Type |
|---|---|
| **TextureOutput** | T3.Core.DataTypes.Texture2D |
| **ShaderCode_** | System.String |

