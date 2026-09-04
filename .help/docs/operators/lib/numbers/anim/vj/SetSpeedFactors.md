# SetSpeedFactors

*in [Lib.numbers.anim.vj](README.md)*

Speed factors are multiplied to most animation operators like [AnimValue] and [Counter].

This is an extremely powerful method to adjust the pace of visuals while staying beat synced.

In NormalizedRates mode...
- ...0.5 means neutral (factor 1).
- ...lower means slower: 1/2 1/4 1/8 1/16 frozen
- ...larger means faster x2 x4 x8 x16 x32

SpeedFactorA is applied to:
[Counter]

SpeedFactorB is applied to:
[AnimValue]
[AnimVec2]
[AnimVec3]

To keep things flexible it supports Image and Commands types (but not crossing between them).

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Commands** (Command Required) | — |
| **Texture** (Texture2D) | — |
| **SpeedFactorA** (Single) | — |
| **SpeedFactorB** (Single) | — |
| **ApplyAs** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **OutputCommand** | T3.Core.DataTypes.Command |
| **Output** | T3.Core.DataTypes.Texture2D |

