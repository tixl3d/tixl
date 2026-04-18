# SerialOutput

*in [Lib.io.serial](README.md)*

Sends data to a serial port.

This operator can be used to send data to hardware devices like Arduino, DMX interfaces, or other custom electronics using the serial protocol.

Multiple values can be combined into a single message using the `MessageParts` input. 

Tips:
- Use `SendOnChange` to automatically send data when `MessageParts` changes.
- Use `SendTrigger` for manual control over when messages are sent.
- `AddLineEnding` is often required for text-based communication protocols.

AKA: arduino, rs232, uart, com

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **PortName** (String) | The name of the serial port, e.g., 'COM3'. A dropdown shows available ports. |
| **BaudRate** (Int32) | The communication speed for the serial port. This has to match the setting on the connected device. Common values are 9600, 19200, 38400, 57600, 115200. |
| **MessageParts** (String) | A list of strings or values that will be joined together to form the message. Non-string values will be converted. |
| **Separator** (String) | A string used to join the `MessageParts` together. |
| **SendTrigger** (Boolean) | A trigger to manually send the message. The message is sent when this value changes from 0 to 1. |
| **SendOnChange** (Boolean) | If enabled, a message is sent automatically whenever the content of `MessageParts` changes. |
| **AddLineEnding** (Boolean) | If enabled, a carriage return and newline character (<br/>) is appended to the message. This is required by many devices. |
| **Connect** (Boolean) | Connects or disconnects from the serial port. This is useful to free the port for other applications without deleting the operator. |

## Outputs
| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Command |
| **IsConnected** | System.Boolean |

