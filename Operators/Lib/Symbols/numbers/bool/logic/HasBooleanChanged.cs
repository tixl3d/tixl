using T3.Core.Utils;

namespace Lib.numbers.@bool.logic;

[Guid("57351603-f472-445f-b94b-e8f538f85517")]
internal sealed class HasBooleanChanged : Instance<HasBooleanChanged>
{
    [Output(Guid = "7c50b4ec-3ae8-45a0-8a66-d21d3f38de12")]
    public readonly Slot<bool> HasChanged = new();

        
    public HasBooleanChanged()
    {
        HasChanged.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newValue = Value.GetValue(context);

        bool hasChanged = (Modes)Mode.GetValue(context).Clamp(0, Enum.GetNames(typeof(Modes)).Length - 1) switch
                              {
                                  Modes.Changed   => newValue != _lastValue,
                                  Modes.Increased => newValue != _lastValue && newValue,
                                  Modes.Decreased => newValue != _lastValue && !newValue,
                                  _               => throw new ArgumentOutOfRangeException()
                              };

        HasChanged.Value = hasChanged;
        _lastValue = newValue;
    }

    private bool _lastValue;

    [Input(Guid = "C92D216A-B2E4-401C-A12E-D75FF975B7BD")]
    public readonly InputSlot<bool> Value = new();

    [Input(Guid = "d9a51e1d-eeb7-46c3-8e93-0a4a073e819b", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();


    private enum Modes
    {
        Changed,
        Increased,
        Decreased,
    }
}