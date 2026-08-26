
namespace Lib.point._cpu;

[Guid("478522e1-5683-4db1-a7dc-db59838eca2a")]
internal sealed class RepeatAtPointsCpu : Instance<RepeatAtPointsCpu>
{
    [Output(Guid = "0e874f9d-352c-435f-a6d2-d7c8d01e2205")]
    public readonly Slot<StructuredList> ResultList = new();

    public RepeatAtPointsCpu()
    {
        ResultList.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var sourcePoints = SourcePoints.GetValue(context) as StructuredList<Point>;
        var destinationPoints = DestinationsPoints.GetValue(context) as StructuredList<Point>;

        if (sourcePoints == null || destinationPoints == null
                                 || sourcePoints.NumElements == 0 || destinationPoints.NumElements == 0)
        {
            _pointList.SetLength(0);
            ResultList.Value = _pointList;
            return;
        }

        var count = sourcePoints.NumElements * destinationPoints.NumElements;

        if (_pointList.NumElements != count)
        {
            _pointList.SetLength(count);
        }
            
        for (var destinationIndex = 0; destinationIndex < destinationPoints.NumElements; destinationIndex++)
        {
            var destination = destinationPoints.TypedElements[destinationIndex];
                
            for (var sourceIndex = 0; sourceIndex < sourcePoints.NumElements; sourceIndex++)
            {
                var source = sourcePoints.TypedElements[sourceIndex];
                _pointList.TypedElements[destinationIndex * sourcePoints.NumElements + sourceIndex]
                    = new Point()
                          {
                              Position = destination.Position + Vector3.Transform(source.Position, destination.Orientation),
                              F1 = source.F1,
                              Orientation = Quaternion.Multiply(destination.Orientation, source.Orientation),
                          };
            }
        }

        ResultList.Value = _pointList;
    }

    private readonly StructuredList<Point> _pointList = new(10);

    [Input(Guid = "6FFEC88A-F5B9-4D2F-8B03-A2D3DA9C6B8C")]
    public readonly InputSlot<StructuredList> SourcePoints = new();

    [Input(Guid = "26EA6E28-E093-484C-9635-5C4AC0EFDFB7")]
    public readonly InputSlot<StructuredList> DestinationsPoints = new();
}