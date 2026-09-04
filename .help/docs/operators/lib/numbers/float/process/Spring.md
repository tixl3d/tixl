# Spring

*in [Lib.numbers.float.process](README.md)*

Adds a bounce effect to the incoming value.

Also known as jump, bounce, wobble, rubber, flex

A similar op: [Damp] [SpringVec2] [SpringVec3]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Value** (Single Required) | Value Input |
| **Tension** (Single) | Defines the rigidity of the spring.<br/>Lower values produce slower and more intense bounce. |
| **Strength** (Single) | Defines the strength of the spring.<br/>Higher values produce faster and longer bouncing. |
| **UseAppRunTime** (Boolean) | Checking this box will cause the spring calculations to use RunTime instead of FxTime for their timing.<br/><br/>This means the damping shape/speed will not be affected by any changes you might make to playback speed (such as pausing or reversing the timeline). |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |

