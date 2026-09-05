namespace Lib.@string.list;

[Guid("a0fcf7ed-1f14-4a8b-a57e-99e5b2407b1b")]
internal sealed class SplitString : Instance<SplitString>
{
    [Output(Guid = "52745502-3b69-4b2e-be47-d2660fe08e48")]
    public readonly Slot<List<string>> Fragments = new();

    [Output(Guid = "6C78D167-F9F5-43A0-8CD6-8A8B0A34067E")]
    public readonly Slot<int> Count = new();
        
    public SplitString()
    {
        Fragments.UpdateAction += Update;
        Count.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var split = Split.GetValue(context);
        var c = (split.Length == 0 || split == "\\n")  
                    ? '\n'
                    : split[0];
            
        var str = String.GetValue(context);
        if (string.IsNullOrEmpty(str))
        {
            Fragments.Value = _emptyList;
            return;
        }
            
        Fragments.Value = str.Split(c).ToList();
        Count.Value = Fragments.Value.Count;
            
        Fragments.DirtyFlag.Clear();
        Count.DirtyFlag.Clear();
    }

    private readonly List<string> _emptyList = new();

    [Input(Guid = "b1fd8b37-140e-487f-bfe2-bc426d8fe439")]
    public readonly InputSlot<string> String = new("Line\nLine");
        
    [Input(Guid = "c54e4b16-b185-41f8-bc50-230b7624d093")]
    public readonly InputSlot<string> Split = new("\n");
}