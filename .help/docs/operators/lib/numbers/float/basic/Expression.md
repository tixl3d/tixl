# Expression

*in [Lib.numbers.float.basic](README.md)*

Evaluates a math formula with variables a, b, c, d and outputs the result as a float

The formula is only recompiled when its text changes. Names are not case-sensitive. An invalid formula logs a warning and outputs NaN.

Examples: `a + b`, `(1 - a) + (b * 2 * c) - d / Sin(c)`, `Lerp(a, b, c)`, `Max(a, b) * Sin(c * PI)`

Operators: `+`, `-`, `*`, `/`, `%` and parentheses

Trigonometry: `Sin`, `Cos`, `Tan`, `Asin`, `Acos`, `Atan`, `Atan2`, `Sinh`, `Cosh`, `Tanh`

Power/Exponential: `Sqrt`, `Pow`, `Exp`, `Log`, `Log10`

Rounding: `Abs`, `Floor`, `Ceil`, `Ceiling`, `Round`

Min/Max: `Min`, `Max`, `Clamp`, `Lerp`, `Sign`

Conversion: `DegToRad`, `RadToDeg`

Constants: `PI`, `E`

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **Formula** (String Relevant) | The formula to evaluate. Variables a, b, c, d are available. Math functions: Sin, Cos, Tan, Sqrt, Pow, Log, Abs, Floor, Ceil, Round, Min, Max, Clamp, Lerp, etc. |
| **A** (Single) | Variable a - available in the expression |
| **B** (Single) | Variable b - available in the expression |
| **C** (Single) | Variable c - available in the expression |
| **D** (Single) | Variable d - available in the expression |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
