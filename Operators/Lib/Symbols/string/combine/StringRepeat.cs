using T3.Core.Utils;

namespace Lib.@string.combine;

[Guid("04d0d6d7-8c40-4d18-aa44-6806c51fe139")]
internal sealed class StringRepeat : Instance<StringRepeat>
{
    [Output(Guid = "568d04ca-3b13-4ed3-93f9-f0fbf804d96e")]
    public readonly Slot<string> Result = new();

    public StringRepeat()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var content = Fragment.GetValue(context);
        var count =  Count.GetValue(context).Clamp(0,1000);
        if (count == 0 || string.IsNullOrEmpty(content))
        {
            Result.Value = string.Empty;
        }
        else
        {
            Result.Value =  new StringBuilder().Insert(0, content, count).ToString();
        }
    }
        
    [Input(Guid = "3804f72d-7541-4877-a417-d029a20035d8")]
    public readonly InputSlot<string> Fragment = new();
        
        
    [Input(Guid = "DA681B55-9537-4D86-B31A-38223CC0BC71")]
    public readonly InputSlot<int> Count = new();
        
}