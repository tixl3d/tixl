# OmtInput

*in [Lib.io.video](README.md)*

Receives one OMT source and publishes it as a texture. Sources are found through OMT's DNS-SD
discovery, so a sender that starts after the operator was set up is picked up by the library on its
own.

Pick a source from the **SourceName** dropdown (entries are `MACHINE (Source Name)`), or type the
advertised address. **TriggerReconnect** recreates the receiver after a sender changed its address.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **SourceName** (String) | The advertised OMT source to receive. The dropdown lists the sources discovery currently sees. |
| **TriggerReconnect** (Boolean) | Recreate the receiver; self-clears. |

## Outputs
| Name | Type |
|---|---|
| **Texture** | T3.Core.DataTypes.Texture2D |

**See also:** [OmtOutput](OmtOutput.md), [NdiInput](NdiInput.md), [SpoutInput](SpoutInput.md)

---
*Generated from operator definition in Lib.io.video*
