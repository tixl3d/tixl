# MouseInput

*in [Lib.io.input](README.md)*

Allows the real-time manipulation of any values using the mouse / trackpad

If the mouse pointer is moved over the output window, the values of the X and Y axes of the mouse movements are evaluated and can control any functions. The left mouse button can also be evaluated as a boolean.

Useful combinations: [Vec2ToVec3] [Vector2Components] [BoolToFloat] [Transform] [TransformMesh]

Also see: [KeyboardInput] [MidiInput]

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **DoUpdate** (Boolean) | Allows forcing permanent updating |
| **OutputMode** (Int32) | Defines the method in which the pointer position is to be evaluated. |
| **Scale** (Single) | Multiplies / amplifies the output values |

## Outputs
| Name | Type |
|---|---|
| **Position** | System.Numerics.Vector2 |
| **IsLeftButtonDown** | System.Boolean |
| **Position3d** | System.Numerics.Vector3 |

