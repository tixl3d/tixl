# WledInstance

*in [Lib.io.dmx](README.md)*

Discovers WLED devices on the network via mDNS/Zeroconf and exposes their E1.31/sACN sync settings as outputs.

Use this operator to find WLED controllers and automatically configure sACN parameters (universe, start address, sync mode, timeout). Chain the outputs into a [SacnOutput] operator to send DMX data to the discovered WLED device.

All parameters have both inputs (for manual/static values) and outputs (for chaining). When discovery is enabled and a matching instance name is selected, the discovered values override the manual inputs.

Features:
- WLED device discovery via mDNS/Zeroconf
- Fetches sync settings (universe, start address, sync enabled, sync mode, timeout) from each device's web UI
- Fetches LED count from each device's `/json/` API (`info.leds.count`)
- Static input values work without discovery
- Outputs can be chained to other operators (e.g. SacnOutput)
- Discovery is shared across all WledInstance instances

Tips:
- Enable `Discover` and select a device from the `InstanceName` dropdown to auto-configure all parameters
- Without discovery, set `Universe`, `StartAddress`, `Multicast`, etc. manually
- Chain `IpAddress` into SacnOutput's `TargetIpAddress`, `Universe` into `SyncUniverse`, etc.

AKA: wled, discovery, mdns, zeroconf, e1.31, sacn, led, dmx

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Discover** (Boolean) | If enabled, discovers WLED devices on the network via mDNS/Zeroconf and populates the `InstanceName` dropdown. When a device is selected, its sync settings override the manual inputs. |
| **InstanceNameInput** (String) | The name of the WLED instance to use. Select from the dropdown (requires Discover enabled) or type a name manually. When discovery is active and the name matches a discovered device, the device's settings are used. |
| **IpAddressInput** (String) | The IP address of the WLED device. Set manually or leave empty when using discovery (the discovered IP will be used). |
| **UniverseInput** (Int32) | The sACN/E1.31 universe number. When discovery finds a matching device, this is set from the device's sync universe setting. |
| **StartAddressInput** (Int32) | The DMX start address within the universe. When discovery finds a matching device, this is set from the device's DMX address setting. |
| **LedCountInput** (Int32) | The number of LEDs on the WLED device. Set manually or leave at 0 when using discovery (the discovered count will be used from the device's `/json/` API). |
| **MulticastInput** (Boolean) | Whether Multicast is enabled on the WLED device. When discovery finds a matching device, this is set from the device's sync type setting. |
| **DmxModeInput** (Int32) | The DMX mode (Single RGB, Multi RGB, effect, etc). When discovery finds a matching device, this is set from the device's sync mode setting. |
| **SyncTimeoutInput** (Int32) | The sync timeout in milliseconds. When discovery finds a matching device, this is set from the device's sync timeout setting. |

## Outputs
| Name | Type |
|---|---|
| **InstanceName** | System.String |
| **IpAddress** | System.String |
| **Universe** | System.Int32 |
| **StartAddress** | System.Int32 |
| **LedCount** | System.Int32 |
| **Multicast** | System.Boolean |
| **DmxMode** | System.Int32 |
| **SyncTimeout** | System.Int32 |
