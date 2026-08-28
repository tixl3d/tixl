using T3.Core.Utils;

namespace Lib.numbers.vec2;

[Guid("fb801f10-8f84-4f69-9f7e-66cc7f6b7878")]
internal sealed class DampVec2 : Instance<DampVec2>
{
    [Output(Guid = "A49381B7-F05B-48E6-9205-B10D81DF9671")]
    public readonly Slot<Vector2> Result = new();

        
    public DampVec2()
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
                               1 => DampFunctions.SpringDampVec2(targetVector, _dampedValue, damping, ref _velocity),
                               _ => targetVector,
                           };
        MathUtils.ApplyDefaultIfInvalid(ref _dampedValue, Vector2.Zero);
        MathUtils.ApplyDefaultIfInvalid(ref _velocity, Vector2.Zero);
        Result.Value = _dampedValue;
    }

    private Vector2 _dampedValue;
    private Vector2 _velocity;
    private double _lastEvalTime;
    private readonly float minTimeElapsedBeforeEvaluation = 1 / 1000f;
        
    [Input(Guid = "E5D132E7-018E-4B37-A49B-8274AC988311")]
    public readonly InputSlot<Vector2> Value = new();

    [Input(Guid = "dc4a8e1c-9399-4e06-a3d8-168839085f02")]
    public readonly InputSlot<float> Damping = new();
        
    [Input(Guid = "e5a9f11d-dc51-45bb-9ae0-3ddb6119527d", MappedType = typeof(DampFunctions.Methods))]
    public readonly InputSlot<int> Method = new();
        
}