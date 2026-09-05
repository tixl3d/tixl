namespace Lib.@string.combine;

[Guid("48ab9824-76ca-4238-800f-9cf95311e6c0")]
internal sealed class CombineStrings : Instance<CombineStrings>
{
    [Output(Guid = "{E47BF25E-351A-44E6-84C6-AD3ABC93531A}")]
    public readonly Slot<string> Result = new();

    public CombineStrings()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _stringBuilder.Clear();
        var separator = Separator.GetValue(context).Replace("\\n", "\n");

        var isFirst = true;
        foreach (var input in Input.GetCollectedTypedInputs())
        {
            if (!isFirst && !string.IsNullOrEmpty(separator))
                _stringBuilder.Append(separator);

            var t = input.GetValue(context);
            if(!string.IsNullOrEmpty(t))
                _stringBuilder.Append(t);
                
            isFirst = false;
        }
        Result.Value = _stringBuilder.ToString();
        Input.DirtyFlag.Clear();
    }

    private readonly StringBuilder _stringBuilder = new();

    [Input(Guid = "{B5E72715-9339-484F-B197-5A28CD823798}")]
    public readonly MultiInputSlot<string> Input = new();
        
    [Input(Guid = "C832BA89-F4AE-4C47-B62B-52DA52A09556")]
    public readonly InputSlot<string> Separator = new();

}