# SacnOutput

*in [Lib.io.dmx](README.md)*

Sends DMX data over the network using the sACN (Streaming ACN / E1.31) protocol.

This operator multicasts or unicasts DMX data to sACN-enabled devices like lighting fixtures, nodes, or media servers. It can send multiple DMX universes simultaneously and uses a dedicated background thread for high-performance transmission.

Features:
- Multi-input support: Connect multiple lists to send different universes
- Automatic universe expansion: Lists larger than 512 channels span multiple universes
- Built-in source discovery to find sACN devices on your network
- Configurable priority for HTP (Highest Takes Precedence) merging
- Frame rate limiting to prevent network flooding
- Optional sACN Synchronization packets

Tips:
- For multicast, leave `TargetIpAddress` empty. For unicast, specify the destination IP.
- Use `UniverseChannels` to define the starting universe for each input (auto-expands as needed)
- Enable `SendTrigger` to start continuous transmission; disable to stop.
- Higher `Priority` values (0-200) override lower priority sources on sACN receivers.

AKA: sacn, streaming acn, dmx, artnet, lighting, e1.31

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **InputsValues** (List`1) | A multi-input where each connected input provides DMX channel data for a separate universe. Each input should contain a list of float values (0-255) that are clamped to 0-255. |
| **UniverseChannels** (List`1) | Defines the starting universe number for each input. If an input has more than 512 channels, it automatically spans to the next universe. |
| **LocalIpAddress** (String) | The IP address of the network interface to send from. A dropdown shows available addresses. If you have multiple network cards, make sure to select the one connected to your lighting network. |
| **SendTrigger** (Boolean) | Enables continuous transmission of sACN data. When true, the operator sends packets at the rate specified by `MaxFps`. |
| **Reconnect** (Boolean) | Forces a reconnection of the network socket. Toggle this to reconnect after network changes. |
| **SendUnicast** (Boolean) | If enabled, sends sACN packets as unicast to the `TargetIpAddress`. Otherwise, packets are multicast. |
| **TargetIpAddress** (String) | The destination IP address for unicast sACN packets. If empty, packets are multicast to the standard sACN multicast address. |
| **DiscoverSources** (Boolean) | If enabled, the operator listens for sACN sources on the network and populates the `TargetIpAddress` dropdown with discovered devices. Useful for finding sACN receivers. |
| **Priority** (Int32) | The priority of the sent DMX data (0-200). Higher priority data will override lower priority data on the receiving node. Default is 100. |
| **SourceName** (String) | A user-defined name for this sACN source, which can help identify it on the network. |
| **MaxFps** (Int32) | Limits the maximum update rate in frames per second. A value of 0 means no limit. |
| **EnableSync** (Boolean) | If enabled, an sACN Synchronization packet is sent to the `SyncUniverse`. This can be used to synchronize updates across multiple sACN receivers. |
| **SyncUniverse** (Int32) | The universe to which the sACN Synchronization packet is sent. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |

