# SerialInput

*in [Lib.io.serial](README.md)*

Receives data from a serial port.

This operator listens for incoming data on a specified serial port, allowing communication with external hardware devices such as microcontrollers (e.g., Arduino) or other serial-enabled equipment. It can parse incoming data as individual lines or as a continuous string.

Tips:
- Ensure the `BaudRate` and `PortName` match the settings of the sending device.
- Use `ReceivedLines` to process multiple incoming messages, especially when the sending device terminates messages with a line ending.
- `WasTrigger` can be used to trigger other operators when new data arrives.

AKA: arduino, rs232, uart, com, serial reader

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **PortName** (String) | The name of the serial port to listen on, e.g., 'COM3'. A dropdown shows available ports. |
| **BaudRate** (Int32) | The communication speed for the serial port. This must match the setting on the connected device. Common values include 9600, 19200, 38400, 57600, 115200. |
| **ListLength** (Int32) | The maximum number of received lines to store in the `ReceivedLines` output. |
| **Connect** (Boolean) | Connects or disconnects from the serial port. This can be used to free the port for other applications without deleting the operator. |

## Outputs
| Name | Type |
|---|---|
| **ReceivedString** | System.String |
| **ReceivedLines** | System.Collections.Generic.List`1[System.String] |
| **WasTrigger** | System.Boolean |
| **IsConnected** | System.Boolean |

