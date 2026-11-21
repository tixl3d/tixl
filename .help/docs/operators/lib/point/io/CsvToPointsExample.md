# CsvToPointsExample

*in [Lib.point.io](README.md)*

Loads a CSV file and renders its columns as points.

Every point attribute is read from a named column: Position X/Y/Z, rotation around Z, a uniform scale for all three axes, RGBA color in 0..255 and the radial phase mapped to F1. The sample dataset is a 21×21 ripple with per-point color and size; the points are drawn into a render target so the example can be used like any other image source.

Try changing CsvFile to your own file, or open the operator's parameter panel and pick different columns in the mapping dropdowns.

## Outputs

| Name | Type |
|---|---|
| **Output** | T3.Core.DataTypes.Texture2D |


