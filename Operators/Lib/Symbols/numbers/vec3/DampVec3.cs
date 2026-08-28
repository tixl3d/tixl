using T3.Core.Utils;

namespace Lib.numbers.vec3;

[Guid("671f8151-9ec1-4f02-9ad1-b8a2d70d3d68")]
internal sealed class DampVec3 : Instance<DampVec3>
{
    [Output(Guid = "87C0CCAE-46DA-460E-BE9D-C4B4E5753D0B", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Result = new();

        
    public DampVec3()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var targetVector = Value.GetValue(context);
        var damping = Damping.GetValue(context);

        var currentTime = context.LocalFxTime;
        if (Math.Abs(currentTime - _lastEvalTime) < minTimeElapsedBeforeEvaluation)
            return;

        _lastEvalTime = currentTime;

        var method = Method.GetValue(context).Clamp(0, 1);
        _dampedValue = method switch
                           {
                               0 => MathUtils.Lerp(targetVector, _dampedValue, damping),
                               1 => DampFunctions.SpringDampVec3(targetVector, _dampedValue, damping, ref _velocity),
                               _ => targetVector,
                           };
        MathUtils.ApplyDefaultIfInvalid(ref _dampedValue, Vector3.Zero);
        MathUtils.ApplyDefaultIfInvalid(ref _velocity, Vector3.Zero);
        Result.Value = _dampedValue;
    }

    private Vector3 _dampedValue;
    private Vector3 _velocity;
    private double _lastEvalTime;
    private readonly float minTimeElapsedBeforeEvaluation = 1 / 1000f;
        
    [Input(Guid = "7E5DDFA7-B305-4744-A7EF-0F70C2B9741E")]
    public readonly InputSlot<Vector3> Value = new();

    [Input(Guid = "490fc348-1b5d-47da-852b-8fd6d272b0b5")]
    public readonly InputSlot<float> Damping = new();
        
    [Input(Guid = "d0918492-8d43-4762-b21f-fd1bcd2f0473", MappedType = typeof(DampFunctions.Methods))]
    public readonly InputSlot<int> Method = new();
        
}