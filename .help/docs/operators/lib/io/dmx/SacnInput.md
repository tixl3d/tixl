# SacnInput

*in [Lib.io.dmx](README.md)*

Receives DMX data over the network using the sACN (Streaming ACN) protocol.

This operator listens for sACN packets on the local network, allowing you to receive DMX data from lighting consoles, media servers, or other sACN-enabled devices. It can listen to multiple DMX universes simultaneously.

Tips:
- Select the correct `LocalIpAddress` for your network interface.
- `StartUniverse` and `NumUniverses` define the range of DMX universes to listen to.
- The output is a list of lists. The outer list corresponds to the universes, and each inner list contains the 512 channel values for that universe.

AKA: sacn, streaming acn, dmx, artnet, lighting

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Active** (Boolean) | Enables or disables listening for sACN packets. |
| **LocalIpAddress** (String) | The IP address of the network interface to listen on. A dropdown shows available addresses. If you have multiple network cards, make sure to select the one connected to your lighting network. |
| **NumUniverses** (Int32) | The number of consecutive DMX universes to listen for, starting from `StartUniverse`. |
| **PrintToLog** (Boolean) | If enabled, prints status information and errors to the T3 log. Useful for debugging. |
| **StartUniverse** (Int32) | The first DMX universe to listen for. Must be 1 or greater. |
| **Timeout** (Single) | The time in seconds after which a universe is considered offline if no new data is received. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Collections.Generic.List`1[System.Int32] |

