using T3.Core.Utils;

namespace Lib.numbers.@float.process;

[Guid("587dbb73-fd79-4481-a79e-f77055abda9a")]
internal sealed class FreezeValue : Instance<FreezeValue>
{
    [Output(Guid = "5bf37afc-d45a-42d0-8f87-905fb5ee013d")]
    public readonly Slot<float> Result = new();
        
    [Output(Guid = "A125BD4B-3926-4373-9FD2-26620D7CEDD3")]
    public readonly Slot<float> DeltaSinceFreeze = new();

        
    public FreezeValue()
    {
        Result.UpdateAction += Update;
        DeltaSinceFreeze.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newValue = Value.GetValue(context);
        var freeze = Freeze.GetValue(context);
        var mode = Mode.GetEnumValue<Modes>(context);
        var wasTriggered = MathUtils.WasTriggered(freeze, ref _freeze);
            
        if (mode == Modes.FreezeWhileTrue)
        {
            if (!freeze)
            {
                _frozenValue = newValue;
            }
        }
        else
        {
            if (wasTriggered)
            {
                _frozenValue = newValue;
            }
        }
            
        Result.Value = _frozenValue;
        DeltaSinceFreeze.Value = newValue - _frozenValue;
    }

    private float _frozenValue;
    private bool _freeze;

    [Input(Guid = "7d64e809-3280-47fa-ad0f-218c6081534f")]
    public readonly InputSlot<float> Value = new();

    [Input(Guid = "b53df009-a0eb-46c9-90db-04d8ccdb0ae4")]
    public readonly InputSlot<bool> Freeze = new();

    [Input(Guid = "9AD1267B-6D0A-43F4-A4E9-0F77659DDD44", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    private enum Modes
    {
        FreezeWhileTrue,
        UpdateWhenSwitchingToTrue,
    }
}