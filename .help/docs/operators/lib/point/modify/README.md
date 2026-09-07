# Lib.point.modify

## Operators

- [**AddNoise**](AddNoise.md) — Creates a new buffer by resampling the connected points. This can be useful for increasing resolution or smoothing out hard edges.
- [**AttributesFromImageChannels**](AttributesFromImageChannels.md) — Some test.
- [**ClearSomePoints**](ClearSomePoints.md) — Override a fraction of points with separators to insert gaps into lines.
- [**CustomPointShader**](CustomPointShader.md) — A very fast method of writing simple compute shaders to manipulate the connected points.
- [**FilterPoints**](FilterPoints.md) — Selects (i.e., picks) points based on the given criteria.
- [**LinearSamplePointAttributes**](LinearSamplePointAttributes.md) — A variation of [SamplePointAttributes] that uses the point index instead of texture mapping.
- [**MapPointAttributes**](MapPointAttributes.md) — Sets the points attribute and color from input attributes. This can be very powerful to remap point attributes.
- [**MoveToSDF**](MoveToSDF.md) — Moves points to the nearest SDF surface.
- [**PointAttributeFromNoise**](PointAttributeFromNoise.md) — Changes point attributes with a built-in noise function.
- [**PointColorWithField**](PointColorWithField.md) — Uses a color field to set point colors from their position in that field.
- [**RandomizePoints**](RandomizePoints.md) — Smoothly randomizes various point attributes. It's an extremely versatile operator that provides various options of applying the random modifications and can be smoothly animated.
- [**ResampleLinePoints**](ResampleLinePoints.md)
- [**SamplePointAttributes_v1**](SamplePointAttributes_v1.md) — Samples point attributes from the RGB channels of the connected operator.
- [**SamplePointColorAttributes**](SamplePointColorAttributes.md) — Use a texture to color the points. Same as [SamplePointAttributes] but for colors only
- [**SamplePointsByCameraDistance**](SamplePointsByCameraDistance.md) — Changes the W value / F value of existing points based on their distance to the active camera.
- [**SelectPoints**](SelectPoints.md) — Simulates a selection of points by setting the F1 or F2 attribute.
- [**SelectPointsWithSDF**](SelectPointsWithSDF.md) — Allows selecting and adding values to points in 3D space via SDF Objects/Fields.
- [**SetAttributesWithPointFields**](SetAttributesWithPointFields.md) — Sets various attribute points from the distance of a 2nd (small) set of points.
- [**SetPointAttributes**](SetPointAttributes.md) — Sets various attributes of points
- [**SortPoints**](SortPoints.md) — Sort points by distance to camera, so that the distant sprites can be drawn first, and ones closer to camera get blended correctly
- [**TransformWithImage**](TransformWithImage.md) — Allows modifying various point attributes from an image.

---

*Auto-generated from the operator library.*
