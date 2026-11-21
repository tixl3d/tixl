# Laser Data Model

## LaserPoint

`T3.Core.DataTypes.LaserPoint` (`Core/DataTypes/LaserPoint.cs`) is the currency of the laser operators. It is a packed struct of six 32-bit integers:

| Field | Meaning | Range |
|-------|---------|-------|
| `X`, `Y` | Position | -32768..32767 (ILDA-style 16-bit, origin at center, +Y up) |
| `R`, `G`, `B` | Color | 0..65535 |
| `I` | Intensity (blanking) | 0..65535, **0 = beam off / blanked jump** |

Points flow between operators as a `StructuredList<LaserPoint>`.

Blanking is the convention everything builds on: the laser output's optimization stage uses `I == 0` runs to detect shape segments, and `LaserPoint.CreateBlanked(x, y)` produces the delay points it inserts.
