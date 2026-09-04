# TriggerAnim

*in [Lib.numbers.anim.animators](README.md)*

Generate interactive animation values that can be triggered with a boolean value.
It offers a variety of shapes and modes.

Tips:
- Hold CTRL to directly manipulate rate and shape in the graph.
- Have a look at [HowToAnimate] to get some ideas on how to use this.

An example setup might be:

[HasValueIIncreased]->[TriggerAnim]->[SampleGradient]->[Blob]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Trigger** (Boolean Relevant) | — |
| **Shape** (Int32) | — |
| **AnimMode** (Int32) | — |
| **Duration** (Single) | — |
| **Base** (Single) | — |
| **Amplitude** (Single) | — |
| **Delay** (Single) | — |
| **Bias** (Single) | — |
| **TimeMode** (Int32) | — |
| **UseTriggerVar** (String) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
| **HasCompleted** | System.Boolean |

