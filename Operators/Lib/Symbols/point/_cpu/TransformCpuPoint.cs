//using SharpDX;
using T3.Core.Utils;

namespace Lib.point._cpu;

[Guid("5f2df291-4667-474e-9e84-3b64d4cc0555")]
internal sealed class TransformCpuPoint : Instance<TransformCpuPoint>
{
    [Output(Guid = "fe7ab3fe-5a9f-45b1-ae8d-5a78d6822501")]
    public readonly Slot<StructuredList> ResultPoint = new();

    [Output(Guid = "B1EE31B8-5D02-4B05-8F52-051180FA9A4D")]
    public readonly Slot<Vector3> Position = new();

        
    // [Output(Guid = "71325a3c-64fc-4775-91de-a55f397cefa8")]
    // public readonly Slot<int> Length = new Slot<int>();
    //
    public TransformCpuPoint()
    {
        ResultPoint.UpdateAction += Update;
        Position.UpdateAction += Update;

        _pointList.TypedElements[0] = new Point();
    }


    private void Update(EvaluationContext context)
    {
        var connectedLists = Lists2.CollectedInputs.Select(c => c.GetValue(context)).Where(c => c != null).ToList();
        Lists2.DirtyFlag.Clear();
            
        if (connectedLists.Count != 1)
        {
            return;
        }

        var translation = Translation.GetValue(context);
        var rotation = Rotation.GetValue(context);
        var useIncremental = Incremental.GetValue(context);
        var space = Space.GetEnumValue<Spaces>(context);
            

        if (connectedLists[0] as StructuredList<Point> is var pointList)
        {
                
            if (pointList != null && pointList.NumElements > 0)
            {
                var p = _pointList.TypedElements[0];
                    
                if (!useIncremental)
                {
                    p = pointList.TypedElements[0];
                        
                }

                if (space == Spaces.PointSpace)
                {
                    var q = p.Orientation;
                    var resultQ = q * (new Quaternion(translation,0)) / q;

                    translation.X = resultQ.X;
                    translation.Y = resultQ.Y;
                    translation.Z = resultQ.Z;
                }

                p.Orientation = p.Orientation * Quaternion.CreateFromYawPitchRoll(rotation.X, rotation.Y, rotation.Z);

                p.Position += translation;
                _pointList[0] = p;

                Position.Value = p.Position;
            }
                
            ResultPoint.Value = _pointList;
                
        }
    }

    [Input(Guid = "95B26A7E-2360-4830-B605-DA1BD8AE882A")]
    public readonly InputSlot<Vector3> Translation = new ();
        
    [Input(Guid = "3E7BA85D-48D0-413E-99C7-9A8742C2653E")]
    public readonly InputSlot<Vector3> Rotation = new ();

        
    [Input(Guid = "A5E1FDF0-CE29-4370-A91A-AD41E04BDE4E", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> Space = new();        
        
    [Input(Guid = "E8CADC7E-D45F-43F7-9C85-040DA77A3AF7")]
    public readonly InputSlot<bool> Incremental = new ();
        
    [Input(Guid = "bbbeba31-df6b-4f5f-8d00-71adb5e6f9c6")]
    public readonly MultiInputSlot<StructuredList> Lists2 = new();
        
        
    private enum  Spaces 
    {
        WorldSpace,
        PointSpace,
    }
        
    private readonly StructuredList<Point> _pointList = new(1);
        
}