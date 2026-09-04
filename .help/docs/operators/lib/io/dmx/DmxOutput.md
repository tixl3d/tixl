# DmxOutput

*in [Lib.io.dmx](README.md)*

Sends DMX data to a connected DMX interface.

This operator sends a list of float values as DMX channels to a specified DMX universe via a connected DMX interface (e.g., FTDI-based USB-DMX interfaces). The input `DmxUniverse` should be a list of floats, where each float represents a DMX channel value (0-1).

Tips:
- Ensure the correct `PortName` for your DMX interface is selected.
- The `DmxUniverse` input expects a list of up to 512 float values. Use operators like `PointsToDMXLights` to generate this data.
- `MaxFps` can be used to limit the update rate and reduce CPU usage.

AKA: dmx, light, lighting, ftdi

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Connect** (Boolean) | Connects or disconnects from the DMX interface. This is useful to free the port for other applications without deleting the operator. |
| **DmxUniverse** (List`1) | A list of up to 512 float values (0-1) representing the DMX channel values for a single universe. |
| **MaxFps** (Int32) | Limits the maximum update rate in frames per second. A value of 0 means no limit. |
| **PortName** (String) | The name of the serial port for the DMX interface, e.g., 'COM3'. A dropdown shows available FTDI devices. |

## Outputs
| Name | Type |
|---|---|
| **IsConnected** | System.Boolean |
| **Result** | T3.Core.DataTypes.Command |

