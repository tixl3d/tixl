# Expression

*in [Lib.numbers.float.basic](README.md)*

Evaluates a C# expression with variables `a`, `b`, `c`, `d` and outputs the result as a float.

The expression is compiled at runtime using Roslyn and cached — it only recompiles when the expression text changes.

## Examples

| Expression | Description |
|---|---|
| `a + b` | Simple addition |
| `(1 - a) + (b * 2 * c) - d / Sin(c)` | Combined arithmetic with function |
| `Lerp(a, b, c)` | Linear interpolation between a and b by t=c |
| `Max(a, b) * Sin(c * PI)` | Max of a,b scaled by sine |

## Available Functions

| Category | Functions |
|---|---|
| Trigonometry | `Sin`, `Cos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2`, `Sinh`, `Cosh`, `Tanh` |
| Power/Exponential | `Sqrt`, `Pow`, `Exp`, `Log`, `Log10` |
| Rounding | `Abs`, `Floor`, `Ceil`, `Round` |
| Min/Max | `Min`, `Max`, `Clamp`, `Lerp`, `Sign` |
| Conversion | `DegToRad`, `RadToDeg` |
| Constants | `PI`, `E` |

All `System.MathF` functions are also available via `using static System.MathF`.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **ExpressionInput** (String Relevant) | The C# expression to evaluate. Variables a, b, c, d are available. |
| **A** (Single) | Variable a — available in the expression |
| **B** (Single) | Variable b — available in the expression |
| **C** (Single) | Variable c — available in the expression |
| **D** (Single) | Variable d — available in the expression |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
