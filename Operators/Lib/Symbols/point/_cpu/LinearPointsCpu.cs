using T3.Core.Utils;

//using Quaternion = SharpDX.Quaternion;
//using Vector4 = SharpDX.Vector4;

namespace Lib.point._cpu;

[Guid("796a5efb-2ccf-4cae-b01c-d3f20a070181")]
internal sealed class LinearPointsCpu : Instance<LinearPointsCpu>
{
    [Output(Guid = "C8E35D0A-7B10-42D8-9984-006502195FDE")]
    public readonly Slot<StructuredList> PointList = new();
        
    [Output(Guid = "A67DF589-3C51-49A7-805D-5CC0657D491C")]
    public readonly Slot<Point[]> Result = new();


    public LinearPointsCpu()
    {
        PointList.UpdateAction += Update;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var countX = Count.GetValue(context).Clamp(1, 10000);

        var count = countX;
        if (_points.Length != count)
        {
            _points = new Point[count];
            _pointList.SetLength(count);
        }

        var startP = Start.GetValue(context);
        var endP = Offset.GetValue(context);
        var startW = StartW.GetValue(context);
        var scaleW = OffsetW.GetValue(context);

        var startPoint = new Vector3(startP.X, startP.Y, startP.Z);
        var offset = new Vector3(endP.X, endP.Y, endP.Z);
        var index = 0;
        for (var x = 0; x < countX; x++)
        {
            var fX = x / (float)countX;
            _points[index].Position = Vector3.Lerp(startPoint, startPoint + offset, fX);
            _points[index].Orientation = Quaternion.Identity;
            _points[index].F1 = MathUtils.Lerp(startW, startW + scaleW, fX);
            _pointList[index] = _points[index]; 
            index++;
        }

        Result.Value = _points;
        PointList.Value = _pointList;
    }

    private Point[] _points = new Point[0];
    private readonly StructuredList<Point> _pointList = new(10);

    [Input(Guid = "A32B0085-4F83-4240-845C-D663240A738C")]
    public readonly InputSlot<Vector3> Start = new();

    [Input(Guid = "4E3863D3-E295-472A-99BB-4C579A4FFD7B")]
    public readonly InputSlot<float> StartW = new();

    [Input(Guid = "91D2F5B3-D2C4-406B-AB4C-EBB09951538A")]
    public readonly InputSlot<Vector3> Offset = new();

    [Input(Guid = "668C5640-A212-40E0-B68E-D78DBCDDE33A")]
    public readonly InputSlot<float> OffsetW = new();

    [Input(Guid = "759BFAAC-13DD-478A-A4DB-FE52B94CDAEC")]
    public readonly InputSlot<int> Count = new();
}