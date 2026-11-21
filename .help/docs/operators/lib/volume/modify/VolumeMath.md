# VolumeMath

*in [Lib.volume.modify](README.md)*

Arithmetic between two volumes or between a volume and a constant.

Add, Subtract, Multiply, Divide, Min and Max. With UseConstant off, B participates and voxels missing from either volume read as their background. Division by a value close to zero yields zero.

AKA: add, subtract, multiply, divide, min, max, combine

Tips
- Adding a constant to a signed distance field moves its surface inward; subtracting moves it outward.
- Max of two level sets is their intersection and Min is their union.

## Input Parameters

| Name (Relevancy & Type) | Description |
|---|---|
| **Operation** (Int32) | Add, Subtract, Multiply, Divide, Min or Max. |
| **VolumeB** (VdbVolume) | The second operand. Ignored when UseConstant is on. |
| **UseConstant** (Boolean) | Use the Constant instead of volume B. |
| **Constant** (Single) | The value used as the second operand when UseConstant is on. |
| **VolumeA** (VdbVolume) | The first operand and the grid that defines the result. |

## Outputs

| Name | Type |
|---|---|
| **Result** | T3.Core.DataTypes.Volume.VdbVolume |


