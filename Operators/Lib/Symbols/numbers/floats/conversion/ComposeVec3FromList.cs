using T3.Core.Utils;

namespace Lib.numbers.floats.conversion;

[Guid("a52070ce-7110-439c-84e7-01f2a883b83f")]
internal sealed class ComposeVec3FromList : Instance<ComposeVec3FromList>
{
    [Output(Guid = "78B4E13F-78BD-4478-9263-2C77D9284A07")]
    public readonly Slot<Vector3> Result = new();

    public ComposeVec3FromList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var indexForX = IndexForX.GetValue(context);
        var indexForY = IndexForY.GetValue(context);
        var indexForZ = IndexForZ.GetValue(context);

        _inputRange = InputRange.GetValue(context);
        _outputRange = OutputRange.GetValue(context);
        _damping = SpringDamping.GetValue(context);
        _springConstant = 100f / (_damping + 0.001f);
            
        var list = Input.GetValue(context);
        Result.Value = new Vector3(
                                   PickValue(list, indexForX, ref _dampedX,ref  _dampedXVelocity),
                                   PickValue(list, indexForY, ref _dampedY,ref  _dampedYVelocity),
                                   PickValue(list, indexForZ, ref _dampedZ,ref  _dampedZVelocity)
                                  );
    }

    private float PickValue(List<float> list, int index,  ref float damped, ref float dampVelocity)
    {
        if (list == null || index < 0 || index >= list.Count)
            return float.NaN;

        var f = list[index];
        if (float.IsNaN(damped))
            damped = f;
        if (float.IsNaN(dampVelocity))
            dampVelocity = 0;
            
        var remapped = (float)MathUtils.Remap(f, _inputRange.X, _inputRange.Y, _outputRange.X, _outputRange.Y);
        if(_damping == 0)
            return remapped;

        damped = MathUtils.SpringDamp(remapped, damped, ref dampVelocity, _springConstant);
        return damped;
    }

    private float _dampedX, _dampedXVelocity;
    private float _dampedY, _dampedYVelocity;
    private float _dampedZ, _dampedZVelocity;

    private Vector2 _inputRange;
    private Vector2 _outputRange;
    private float _springConstant;
    private float _damping;
        
        
    [Input(Guid = "5166e16a-7366-4907-8b9d-5a9f81a93864")]
    public readonly InputSlot<List<float>> Input = new(new List<float>(20));

    [Input(Guid = "6BDF6F11-01A9-45DD-9318-A447D799C8B8")]
    public readonly InputSlot<int> IndexForX = new(0);

    [Input(Guid = "B74C8E3A-3952-47E9-A41E-4F1D78E3F3BF")]
    public readonly InputSlot<int> IndexForY = new(1);

    [Input(Guid = "3BFF7193-765C-4962-95F5-3E659ED18ED0")]
    public readonly InputSlot<int> IndexForZ = new(2);
        
    [Input(Guid = "8D5B71AC-5829-44F1-800C-E27A37EAB60F")]
    public readonly InputSlot<Vector2> InputRange = new();

    [Input(Guid = "A9B1EDDE-AD70-4C65-8ED6-473577FE59A7")]
    public readonly InputSlot<Vector2> OutputRange = new();

    [Input(Guid = "B529D322-B7A4-4711-8391-E71F196B90B5")]
    public readonly InputSlot<float> SpringDamping = new();

        
}