using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.flow.context;

[Guid("b7731197-b922-4ed8-8e22-bc7596c64f6c")]
internal sealed class GetPosition : Instance<GetPosition>
{
    [Output(Guid = "C809C405-81D3-44CA-A39C-C8DFA6AB3205")]
    public readonly Slot<Command> UpdateCommand = new();

    [Output(Guid = "BB81C71D-9E19-4371-8995-BD61AE426A16")]
    public readonly Slot<Vector3> Position = new();

    [Output(Guid = "ACE050AC-8E49-409A-A194-4CB3192148CA")]
    public readonly Slot<Vector3> Scale = new();

    [Output(Guid = "751E97DE-C418-48C7-823E-D4660073A559")]
    public readonly Slot<Vector4[]> ObjectToWorld = new();

    public GetPosition()
    {
        UpdateCommand.UpdateAction += Update;
        Position.UpdateAction += UpdateReturnResults;
        Scale.UpdateAction += UpdateReturnResults;
        ObjectToWorld.UpdateAction += UpdateReturnResults;
    }

    private void UpdateReturnResults(EvaluationContext context)
    {
        Position.Value = _lastPosition;
        Scale.Value = _lastScale;
        ObjectToWorld.Value = _matrix;
    }

    private void Update(EvaluationContext context)
    {
        var space = Space.GetEnumValue<Spaces>(context);
        var matrix = space switch
                    {
                        Spaces.WorldSpace  => context.ObjectToWorld,
                        Spaces.CameraSpace => context.WorldToCamera * context.ObjectToWorld,
                        Spaces.ClipSpace   =>  context.CameraToClipSpace * context.WorldToCamera * context.ObjectToWorld,
                        _             => context.ObjectToWorld
                    };

        var offset = PositionOffset.GetValue(context);
        var p = new Vector4(offset.X, offset.Y, offset.Z, 1);
        var pInSpace = Vector4.Transform(p, matrix);
        
        var newPosition = new Vector3(pInSpace.X, pInSpace.Y, pInSpace.Z);
        var changed = Vector3.Distance(_lastPosition, newPosition) > 0.00001f;
        
        if(changed)
        {
            _lastPosition = newPosition;
            Position.DirtyFlag.ForceInvalidate();
        }

        var s = new Vector4(1, 1, 1, 0);
        var sInWorld = Vector4.Transform(s, matrix);
        _lastScale = new Vector3(sInWorld.X, sInWorld.Y, sInWorld.Z);

        _matrix[0] = matrix.Row1();
        _matrix[1] = matrix.Row2();
        _matrix[2] = matrix.Row3();
        _matrix[3] = matrix.Row4();
    }

    private readonly Vector4[] _matrix = new Vector4[4];
    private Vector3 _lastPosition;
    private Vector3 _lastScale;

    private enum Spaces
    {
        WorldSpace,
        CameraSpace,
        ClipSpace,
    }

    [Input(Guid = "045AD01F-DD04-43D0-8193-5CB4C25ADD9B", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> Space = new();
    
    [Input(Guid = "D55285F7-9EF9-461B-AC4C-50B80872D397", MappedType = typeof(Spaces))]
    public readonly InputSlot<Vector3> PositionOffset = new();
}