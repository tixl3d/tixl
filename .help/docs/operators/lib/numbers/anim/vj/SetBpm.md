# SetBpm

*in [Lib.numbers.anim.vj](README.md)*

DANGER: Overriding the BPM rate will interfere with your playback and animation speed. 

Only use this operator if you know what you're doing (e.g., in VJ-set context where the BPM rate is provided by an external source like MIDI or OSC)

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SubGraph** (Command Required) | — |
| **BpmRate** (Single) | — |
| **TriggerUpdate** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Commands** | T3.Core.DataTypes.Command |

