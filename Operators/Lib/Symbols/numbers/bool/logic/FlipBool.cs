using T3.Core.Utils;

// ReSharper disable InconsistentNaming

namespace Lib.numbers.@bool.logic;

[Guid("f38311df-d356-4385-8535-96f52a71d53e")]
internal sealed class FlipBool : Instance<FlipBool>
{
    [Output(Guid = "1ff6a7a5-142f-41f9-9802-f6875fbccd44")]
    public readonly Slot<bool> Result = new();

    public FlipBool()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
            
        var isTriggered = MathUtils.WasTriggered(Trigger.GetValue(context), ref _triggered);
        var isReset = ResetTrigger.GetValue(context);
        var defaultValue = DefaultValue.GetValue(context);

        if (isReset)
        {
            // Log.Debug(" exit default " + defaultValue, this);
            Result.Value = defaultValue;
        }
        else if (isTriggered)
        {
            // Log.Debug(" exit true", this);
            Result.Value = !Result.Value;
        }
    }

    private bool _triggered;

    [Input(Guid = "182ed32b-209d-4f6d-ae51-9186883738de")]
    public readonly InputSlot<bool> Trigger = new();

    [Input(Guid = "7a312233-a86b-4d89-ac06-816532e7458f")]
    public readonly InputSlot<bool> ResetTrigger = new();

    [Input(Guid = "ad358d45-0531-4399-a755-c85d5ef01317")]
    public readonly InputSlot<bool> DefaultValue = new();
}