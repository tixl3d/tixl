# CsvToPoints

*in [Lib.point.io](README.md)*

Loads a CSV file and routes its columns into a point buffer.

Every point attribute has a dropdown that selects the CSV column to read from. Columns are matched by header name, so their order does not matter. Unmapped attributes keep their defaults: position 0, scale, color and F1/F2 at 1, and an identity rotation. Numeric cells may carry unit suffixes such as "m", "°" or "W".

Rotation X/Y/Z are Euler angles in degrees by default; switch RotationMode to Quaternion for W/X/Y/Z data.

AKA: csv to points, fixture import

See also: [DataPointConverter]

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **RotationZ** (String) | CSV column for the Z euler angle in degrees, or the Z quaternion component in Quaternion mode. |
| **PositionY** (String) | CSV column copied into Position.Y. Unmapped keeps 0. |
| **ColorA** (String) | CSV column copied into the alpha channel. Unmapped keeps 1. |
| **PositionX** (String) | CSV column copied into Position.X. Unmapped keeps 0. |
| **RotationMode** (Int32) | EulerXYZ treats Rotation X/Y/Z as degrees and ignores W. Quaternion reads all four as quaternion components. |
| **F2** (String) | CSV column copied into the custom F2 attribute. Unmapped keeps 1. |
| **ColorB** (String) | CSV column copied into the blue channel. Unmapped keeps 1. |
| **FilePath** (String) | CSV file to read. Its first row supplies the column names used by the mapping dropdowns. |
| **ColorG** (String) | CSV column copied into the green channel. Unmapped keeps 1. |
| **ScaleZ** (String) | CSV column copied into Scale.Z. Unmapped keeps 1. |
| **ColorR** (String) | CSV column copied into the red channel. Unmapped keeps 1. |
| **RotationY** (String) | CSV column for the Y euler angle in degrees, or the Y quaternion component in Quaternion mode. |
| **PositionZ** (String) | CSV column copied into Position.Z. Unmapped keeps 0. |
| **ScaleX** (String) | CSV column copied into Scale.X. Unmapped keeps 1. |
| **ColorIs0To255** (Boolean) | Divides mapped color channels by 255, for 8-bit color columns. |
| **ScaleY** (String) | CSV column copied into Scale.Y. Unmapped keeps 1. |
| **Reload** (Boolean) | Re-reads the file after its contents changed on disk. |
| **RotationW** (String) | CSV column for the quaternion W component. Ignored in EulerXYZ mode. |
| **RotationX** (String) | CSV column for the X euler angle in degrees, or the X quaternion component in Quaternion mode. |
| **F1** (String) | CSV column copied into the custom F1 attribute. Unmapped keeps 1. |

## Outputs

| Name | Type |
|---|---|
| **Points** | T3.Core.DataTypes.BufferWithViews |


