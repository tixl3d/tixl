# ImageFFT

*in [Lib.image.transform](README.md)*

Discrete Fourier Transform on 2d image. 
RG, BA channels of the texture store two complex values
texture size is expected to be pow2

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Image** (Texture2D) | — |
| **Inverse** (Boolean) | Inverse transform |
| **Direction** (Int32) | — |
| **Normalization** (Int32) | Ortho will scale output by 1/sqrt(N)<br/>Backward will only scale inverse transform by 1/N<br/>Forward will only scale direct (forward) transform by 1/N |

## Outputs
| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |

