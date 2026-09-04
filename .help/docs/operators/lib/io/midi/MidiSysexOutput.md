# MidiSysexOutput

*in [Lib.io.midi](README.md)*

Allow sending midi sysex string to a selected midi device. 

Example: This is useful in the case of the APC40 to change device modes.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **TriggerSend** (Boolean) | Triggers sending the sysex command out to Result |
| **Device** (String) | The device used to send the midi sysex |
| **SysexString** (String) | The sysex string to be sent to the midi device |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

