# SequenceAnim

*in [Lib.numbers.anim.animators](README.md)*

Creates a visual sequencer that can be controlled by adding a string of numbers

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Sequence** (String) | — |
| **SequenceIndex** (Int32) | If there are several number sequences separated by a line break, this value can be used to select the row |
| **UpdateMode** (Int32) | Defines how the sequence is executed |
| **Rate** (Single) | Defines the speed at which the sequence is executed |
| **Phase** (Single) | — |
| **OutputMode** (Int32) | Defines the format in which the values are output |
| **MinValue** (Single) | Scales the lowest value to this limit |
| **MaxValue** (Single) | Scales the highest value to this limit |
| **Bias** (Single) | Bias for the min/max scaling |
| **RecordingMode** (Int32) | Recording value might be useful for tapping in rhythms during a live set.<br/>If there is an input (e.g. MidiInput) connected to recordValue, it will detect hits and write them to the cycle.<br/><br/>The different modes define how recording should work: Either continuous overdubbing or by clearing the complete cycle on recording start.<br/> |
| **RecordValue** (Single) | Input for recording a sequence of values |
| **OverrideTime** (Single) | Overwrites the standard time of the T3 timeline |
| **Direction** (Int32) | — |
| **Interpolation** (Int32) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
| **WasStep** | System.Boolean |

