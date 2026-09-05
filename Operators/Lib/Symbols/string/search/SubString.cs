using T3.Core.Utils;

namespace Lib.@string.search;

[Guid("045e834a-f0ee-432b-8e14-19cadc497577")]
internal sealed class SubString : Instance<SubString>
{
    [Output(Guid = "fba93f4a-aecc-4bcb-9d5a-85b13e362b85")]
    public readonly Slot<string> Result = new();

    public SubString()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var str = InputText.GetValue(context);
        var start = Start.GetValue(context);
        var length = Length.GetValue(context);
            
        var clampStart = start.Clamp(0, str.Length);
        var clampedLength = length.Clamp(0, str.Length - clampStart);
            
        if (string.IsNullOrEmpty(str) || clampedLength == 0 || clampStart >= str.Length)
        {
            Result.Value = string.Empty;
            return;
        }
            
        // Return full string
        if(start == 0 &&  length >= str.Length)
        {
            Result.Value = str;
        }
        else
        {
            try
            {
                Result.Value = str.Substring(clampStart, clampedLength);
            }
            catch (Exception e)
            {
                Log.Warning("Failed to get substring: " + e.Message, this);
            }
        }
    }

    [Input(Guid = "e7f79746-ff87-478e-9755-a8c7b1c34354")]
    public readonly InputSlot<string> InputText = new();

    [Input(Guid = "8BED669E-1141-433F-A8A5-ECBDE812462B")]
    public readonly InputSlot<int> Start = new ();

    [Input(Guid = "1910639E-5A41-4258-9D63-962DDA5EA299")]
    public readonly InputSlot<int> Length = new ();
}