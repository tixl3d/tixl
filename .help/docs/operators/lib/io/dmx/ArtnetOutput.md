# ArtnetOutput

*in [Lib.io.dmx](README.md)*

Sends DMX data over the network using the Art-Net protocol.

This operator broadcasts or unicasts DMX data to Art-Net-enabled devices like lighting fixtures, nodes, or media servers. It can send multiple DMX universes simultaneously and uses a dedicated background thread for high-performance transmission.

Features:
- Multi-input support: Connect multiple lists to send different universes
- Automatic universe expansion: Lists larger than 512 channels span multiple universes
- Built-in ArtPoll discovery to find Art-Net nodes on your network
- Frame rate limiting to prevent network flooding
- Optional ArtSync for synchronized updates

Tips:
- For broadcast, leave `TargetIpAddress` empty. For unicast, specify the destination IP.
- Use `UniverseChannels` to define the starting universe for each input (auto-expands as needed)
- Enable `SendTrigger` to start continuous transmission; disable to stop.

AKA: artnet, dmx, lighting, sacn

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputsValues** (List`1) | A multi-input where each connected input provides DMX channel data for a separate universe. Each input should contain a list of float values (0-255) that are clamped to 0-255. |
| **UniverseChannels** (List`1) | Defines the starting universe number for each input. If an input has more than 512 channels, it automatically spans to the next universe. Auto-expands when you add more inputs. |
| **LocalIpAddress** (String) | The IP address of the network interface to send from. A dropdown shows available addresses. If you have multiple network cards, make sure to select the one connected to your lighting network. |
| **SendTrigger** (Boolean) | A trigger to manually send the Art-Net data. The data is sent when this value changes from 0 to 1. |
| **Reconnect** (Boolean) | Forces a reconnection of the network socket. |
| **SendSync** (Boolean) | If enabled, an ArtSync packet is sent after the DMX data. This can be used to synchronize updates across multiple Art-Net nodes. |
| **SendUnicast** (Boolean) | If enabled, sends Art-Net packets as unicast to the `TargetIpAddress`. Otherwise, packets are broadcast. |
| **EnableArtNet4** (Boolean) | — |
| **TargetIpAddress** (String) | The destination IP address for unicast Art-Net packets. If empty, packets are broadcast to the entire network. |
| **PrintArtnetPoll** (Boolean) | If enabled, sends ArtPoll packets and listens for replies to discover Art-Net nodes on the network. Discovered nodes populate the `TargetIpAddress` dropdown. |
| **MaxFps** (Int32) | Limits the maximum update rate in frames per second. A value of 0 means no limit. |
| **PrintToLog** (Boolean) | If enabled, prints status information and errors to the T3 log. Useful for debugging. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

