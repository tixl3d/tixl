namespace Lib.@string.logic;

[Guid("de9f1dfd-05ec-466f-9f5f-46e7e8da219a")]
internal sealed class HasStringChanged : Instance<HasStringChanged>
{
    [Output(Guid = "e89cfe71-4246-4580-a42c-01d1263cd1c9")]
    public readonly Slot<bool> HasChanged = new();

        
    public HasStringChanged()
    {
        HasChanged.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var newString = Value.GetValue(context);
            
        var hasChanged = newString != _lastString;
        HasChanged.Value = hasChanged;
        _lastString = newString;
            
        HasChanged.DirtyFlag.Trigger = hasChanged ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
    }

    private string _lastString;

    [Input(Guid = "303A7A17-5B3E-4D2E-A5BD-FDE775BE387A")]
    public readonly InputSlot<string> Value = new();
}