using SharpDX;
using T3.Core.Utils;
using T3.Core.Utils.Splines;

namespace Lib.point.combine;

[Guid("edecd98f-209b-423d-8201-0fd7d590c4cf")]
internal sealed class SplinePoints : Instance<SplinePoints>
{
    [Output(Guid = "28b45955-1e05-43a9-87b6-44eabc30bea7")]
    public readonly Slot<BufferWithViews> OutBuffer = new();

    [Output(Guid = "1B0A8C95-CF11-4EF6-BDB7-C54D0CD7BEB7")]
    public readonly Slot<StructuredList> SampledPoints = new();

    public SplinePoints()
    {
        OutBuffer.UpdateAction += Update;
        SampledPoints.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var preSampleSteps = PreSampleSteps.GetValue(context).Clamp(5, 100);
        var upVector = UpVector.GetValue(context);
        var curvature = Curvature.GetValue(context);

        var resultCount = SampleCount.GetValue(context).Clamp(1, 1000);
        try
        {
            //Log.Debug("Update spline", this);
            var pointsCollectedInputs = Points.CollectedInputs;

            var connectedLists = pointsCollectedInputs.Select(c => c.GetValue(context)).Where(c => c != null).ToList();
            Points.DirtyFlag.Clear();

            if (connectedLists.Count < 2)
            {
                _buffer = null;
                OutBuffer.Value = null;
                Log.Warning("Need at least 2 points", this);
                return;
            }

            var sourceItems = connectedLists.Count == 1
                                  ? connectedLists[0].TypedClone()
                                  : connectedLists[0].Join(connectedLists.GetRange(1, connectedLists.Count - 1).ToArray());

            if (sourceItems != null
                && sourceItems.NumElements > 0
                && sourceItems is StructuredList<Point> sourcePointSet)
            {
                var sourcePoints = sourcePointSet.TypedElements;
                _sampledPoints = BezierPointSpline.SamplePointsEvenly(resultCount, preSampleSteps, curvature, upVector, ref sourcePoints);
                _sampledPointsList = new StructuredList<Point>(_sampledPoints);

                // Upload points
                var totalSizeInBytes = _sampledPointsList.TotalSizeInBytes;

                using (var data = new DataStream(totalSizeInBytes, true, true))
                {
                    _sampledPointsList.WriteToStream(data);
                    data.Position = 0;

                    try
                    {
                        ResourceManager.SetupStructuredBuffer(data, totalSizeInBytes, _sampledPointsList.ElementSizeInBytes, ref _buffer);
                    }
                    catch (Exception e)
                    {
                        Log.Error("Failed to setup structured buffer " + e.Message, this);
                        return;
                    }
                }

                ResourceManager.CreateStructuredBufferSrv(_buffer, ref _bufferWithViews.Srv);
                ResourceManager.CreateStructuredBufferUav(_buffer, UnorderedAccessViewBufferFlags.None, ref _bufferWithViews.Uav);

                _bufferWithViews.Buffer = _buffer;
                OutBuffer.Value = _bufferWithViews;
                SampledPoints.Value = _sampledPointsList;
            }
        }
        catch (Exception e)
        {
            Log.Warning("Failed to setup point buffer: " + e.Message, this);
        }

        OutBuffer.DirtyFlag.Clear();
        SampledPoints.DirtyFlag.Clear();
    }

    private Buffer _buffer;
    private readonly BufferWithViews _bufferWithViews = new();
    private Point[] _sampledPoints;
    private StructuredList<Point> _sampledPointsList;

    [Input(Guid = "88AB4088-EFA9-42B7-AFE9-D44A2FF6E58A")]
    public readonly InputSlot<int> SampleCount = new();

    [Input(Guid = "A438A275-7633-4502-9718-E548BB0CE4DE")]
    public readonly InputSlot<int> PreSampleSteps = new();

    [Input(Guid = "336F26E1-853F-41A3-AFE0-E2402A9D7452")]
    public readonly InputSlot<Vector3> UpVector = new();

    [Input(Guid = "2AB21E92-6838-4AC7-8D10-6F8B3C8B5612")]
    public readonly InputSlot<float> Curvature = new();

    [Input(Guid = "02968cef-1a5e-4a7f-b451-b692f4c9b6ab")]
    public readonly MultiInputSlot<StructuredList> Points = new();
}