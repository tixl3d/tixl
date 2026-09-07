# RepetitionPoints

*in [Lib.point.generate](README.md)*

Generate a list of points by repeating a transform operation.
This can be very useful in combination with [DrawMeshAtPoints].

Also, by driving the Offset parameter with an LFO, you can produce seamlessly animated endless scenes. This can be useful for VJ projects.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Count** (Int32) | — |
| **StartPosition** (Vector3) | — |
| **StartW** (Single) | — |
| **Translate** (Vector3) | — |
| **Scale** (Single) | — |
| **Rotate** (Vector3) | — |
| **Pivot** (Vector3) | — |
| **Phase** (Single) | — |
| **AddSeparator** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **ResultList** | T3.Core.DataTypes.StructuredList |

