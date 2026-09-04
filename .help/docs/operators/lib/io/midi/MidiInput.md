# MidiInput

*in [Lib.io.midi](README.md)*

Provides input from connected MIDI devices. By default, the MIDI range from 0 to 127 is mapped to an output range from 0 to 1, which can be adjusted with the parameters. 
For smoothing the output, damping is enabled by default. Reduce the damping parameter to improve latency. 

If you want to react to a range for controls (i.e., to the range of keys on a keyboard), you can use the ControlRange parameter. 
If this is something other than [0, 0], we map its range to the OutputRange (e.g., you could map two octaves of your keyboard to output values from 0 to 1). 

To support controllers that have controllers and note buttons with overlapping control IDs (like the APC Mini), we distinguish between different event types.

Useful combinations: [FloatToInt] [BooltoFloat] [BoolToInt] [FlipFlop] [HasBooleanChanged] [TriggerAnim]

Also see: [MouseInput] [KeyboardInput]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OutputRange** (Vector2) | — |
| **DefaultOutputValue** (Single) | — |
| **Damping** (Single) | — |
| **TeachTrigger** (Boolean) | — |
| **EventType** (Int32) | — |
| **Device** (String) | — |
| **Channel** (Int32) | — |
| **Control** (Int32) | — |
| **ControlRange** (Int2) | — |
| **PrintLogMessages** (Boolean) | — |
| **ResetToDefaultTrigger** (Boolean) | — |
| **LastKnownControllerValue** (Single) | — |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
| **Range** | System.Collections.Generic.List`1[System.Single] |
| **WasHit** | System.Boolean |

