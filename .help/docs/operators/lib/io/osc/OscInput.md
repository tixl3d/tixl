# OscInput

*in [Lib.io.osc](README.md)*

Receives OSC input from a connected server. 

In most scenarios, it is useful to record some input messages and then pick the relevant addresses by using a [SelectFloatFromDict] or [SelectVec2FromDict] operator.

You might want to adjust the default OSC port in the settings window to see a visual indication of incoming messages in the timeline bar.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Port** (Int32) | OSC port to listen on. <br/><br/>Changing this parameter will discard recorded values and reconnect. |
| **Address** (String) | An optional filter to the collected OSC addresses. The first match to this filter will be used for the float output parameter.<br/><br/>This can be useful to directly pick a channel without the need to insert a [SelectFloatFromDict] operator.<br/><br/>This list is automatically filled with all addresses used by incoming OSC messages after connecting to that port. This list is shared between all OscInput operators listening to that port. <br/>Next to the address, there is an indicator for the data type of that address. E.g. <ff> for a vec2 or <ddd> for a double precision Vec3. |
| **UseKeyValuePairs** (Boolean) | Many applications (e.g. Super Collider) send their data bundled as a list of key-value pairs. Enabling this option will use the keys as another segment of the address path. |
| **GroupKeysAsPaths** (String) | — |
| **FilterKeys** (String) | — |
| **SearchFilterKey** (String) | This is an additional method of filtering OSC messages: <br/><br/>If KeyValue pairs are enabled, only messages which have a key with the given pattern will be filtered. |
| **SearchPattern** (String) | This is a regular expression pattern, so it can not only match single strings but a range of values. |
| **PrintLogMessages** (Boolean) | Logs all received OSC messages to the console window, which can be very useful for debugging.<br/><br/>Enabling it can have a performance impact; it can lead to excessive log output. Tip: You can click on a console log message to jump to the Operator that printed the message. |
| **IsListening** (Boolean) | — |

## Outputs
| Name | Type |
|---|---|
| **Contents** | T3.Core.DataTypes.Dict`1[System.Single] |
| **Values** | System.Collections.Generic.List`1[System.Single] |
| **WasTrigger** | System.Boolean |

