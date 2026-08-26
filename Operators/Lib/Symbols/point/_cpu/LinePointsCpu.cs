namespace Lib.point._cpu;

[Guid("a98d7796-6e09-45d1-a372-f3ea55abd359")]
internal sealed class LinePointsCpu : Instance<LinePointsCpu>
{
    [Output(Guid = "ba2e400c-8880-4a4e-9e5b-983ef4846165")]
    public readonly Slot<StructuredList> ResultList = new();

    public LinePointsCpu()
    {
        ResultList.UpdateAction += Update;
        _pointListWithSeparator.TypedElements[2] = Point.Separator();
    }

    private void Update(EvaluationContext context)
    {
        var from = From.GetValue(context);
        var to = To.GetValue(context);
        var w = W.GetValue(context);
        var wOffset = WOffset.GetValue(context);
        var addSeparator = AddSeparator.GetValue(context);

        var rot = Quaternion.CreateFromAxisAngle(new Vector3(0,1,0), (float)Math.Atan2(from.X - to.X, from.Y - to.Y) );
        var array = addSeparator ? _pointListWithSeparator : _pointList;
            
        array.TypedElements[0].Position = from;
        array.TypedElements[0].F1 = w;
        array.TypedElements[0].Orientation = rot;
        array.TypedElements[1].Position = to;
        array.TypedElements[1].F1 = w + wOffset;
        array.TypedElements[1].Orientation = rot;
            
        ResultList.Value = array;
    }

    private readonly StructuredList<Point> _pointListWithSeparator = new(3);
    private readonly StructuredList<Point> _pointList = new(2);
    //private readonly Point Separator;

    [Input(Guid = "CD2B1F6F-1964-4D15-92E0-57B77584301B")]
    public readonly InputSlot<Vector3> From = new();

    [Input(Guid = "37B9A120-F79D-481C-BEA2-A48E3B2A05B7")]
    public readonly InputSlot<Vector3> To = new();
        
    [Input(Guid = "A670BD01-B3BB-4795-9F86-2AB9010F2BEC")]
    public readonly InputSlot<float> W = new();

    [Input(Guid = "4E3D7F66-FA22-4780-835B-1C72DDEC16CE")]
    public readonly InputSlot<float> WOffset = new();

    [Input(Guid = "8ED772FD-182B-455F-BD82-C9585019D035")]
    public readonly InputSlot<bool> AddSeparator = new();
}