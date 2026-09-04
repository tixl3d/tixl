# OscOutput

*in [Lib.io.osc](README.md)*

Send out OSC signals to a receiver.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SendTrigger** (Boolean) | Enables sending messages. If you want a continuous value stream, consider disabling the OnlySendChanges option.<br/><br/>Tip: Reduce the send rate, you can connect an [AnimValue].WasTrigger. |
| **IpAddress** (String) | The target IP address which should be visible. If connection is not possible, check firewall settings and try to use ping. |
| **Port** (Int32) | The target port on the receiver side. |
| **Address** (String) | The target address should start with a forward slash.<br/><br/>Please refer to the OSC documentation. |
| **Reconnect** (Boolean) | — |
| **Values** (Single Relevant) | One or more values to send. You can connect multiple float values to send.<br/><br/>This parameter is ignored (not sent) if default. Click on the parameter name to reset it. |
| **ValuesList** (List`1 Relevant) | — |
| **IntValues** (Int32 Relevant) | One or more values to send. You can connect multiple int values to send.<br/><br/>This parameter is ignored (not sent) if default. Click on the parameter name to reset it. |
| **IntValuesList** (List`1 Relevant) | — |
| **Strings** (String) | One or more strings to send. You can connect multiple strings to send.<br/>The strings will always be sent after the float values.<br/><br/>This parameter is ignored (not sent) if default. Click on the parameter name to reset it. |
| **OnlySendChanges** (Boolean) | If enabled, packages will only be sent if any of the parameters or the address changes. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

