using System;

namespace Lib.geometry;

/// <summary>
/// Produces a ScalarField measuring the distance to the closest point of a point list.
/// The field captures an immutable snapshot of the positions, so evaluation is pure
/// and unaffected by later changes to the list.
/// </summary>
[Guid("54a9af60-51e7-48d0-8afc-aad5ef2d2be6")]
internal sealed class DistanceToPointsField : Instance<DistanceToPointsField>
{
    [Output(Guid = "09a8e3a2-47c1-4e39-bd0a-1b26f926d082")]
    public readonly Slot<ScalarField> Field = new();

    public DistanceToPointsField()
    {
        Field.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        if (Points.GetValue(context) is not StructuredList<Point> pointList || pointList.NumElements == 0)
        {
            Field.Value = null;
            return;
        }

        // Snapshot the positions (skipping separators) so the closure stays pure
        var count = 0;
        var elements = pointList.TypedElements;
        for (var i = 0; i < pointList.NumElements; i++)
        {
            if (!Point.IsSeparator(elements[i]))
                count++;
        }

        if (count == 0)
        {
            Field.Value = null;
            return;
        }

        var snapshot = new Vector3[count];
        var writeIndex = 0;
        for (var i = 0; i < pointList.NumElements; i++)
        {
            if (!Point.IsSeparator(elements[i]))
                snapshot[writeIndex++] = elements[i].Position;
        }

        // Brute force per sample - fine for typical point counts; a spatial grid can
        // replace this transparently behind the same field when profiles demand it.
        Field.Value = new ScalarField((in FieldSample sample) =>
                                      {
                                          var minDistanceSq = float.MaxValue;
                                          for (var i = 0; i < snapshot.Length; i++)
                                          {
                                              var distanceSq = Vector3.DistanceSquared(sample.Position, snapshot[i]);
                                              if (distanceSq < minDistanceSq)
                                                  minDistanceSq = distanceSq;
                                          }

                                          return MathF.Sqrt(minDistanceSq);
                                      });
    }

    [Input(Guid = "cacffef9-8573-4098-bb12-1b2ddcce77de")]
    public readonly InputSlot<StructuredList> Points = new();
}
