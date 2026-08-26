using T3.Core.Utils;

namespace Lib.@string.buffers.transform;

[Guid("7dff2da3-30e3-4ad9-bfc6-d6800f90fca8")]
internal sealed class StringInsert : Instance<StringInsert>
{
    [Output(Guid = "3f9aa46b-e9d5-43fd-b463-fcdd48ded406")]
    public readonly Slot<string> Result = new();

    public StringInsert()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var original = Original.GetValue(context);
        var insert = Insertion.GetValue(context);
        if (string.IsNullOrEmpty(original) || string.IsNullOrEmpty(insert))
            return;

        var maxPosition = original.Length - insert.Length;

        if (maxPosition <= 0)
            return;
            
        var position = Position.GetValue(context);
            
        if (UseModuloPosition.GetValue(context))
        {
            position = Math.Abs(position) % maxPosition;
        }
        else
        {
            position.Clamp(0, maxPosition);
        }

        try
        {
            Result.Value = original.Remove(position, insert.Length).Insert(position, insert);
        }
        catch(Exception e)
        {
            Log.Warning("Failed to insert string:" + e);
        }
    }
        
    [Input(Guid = "11d0a66a-e2e9-4267-9458-f9844a482235")]
    public readonly InputSlot<string> Original = new();
        
    [Input(Guid = "b00a60d3-c525-4e65-8b84-2084521d0d2d")]
    public readonly InputSlot<string> Insertion = new();
        
    [Input(Guid = "B5709B52-E755-4046-8E71-EEAD5A01303A")]
    public readonly InputSlot<int> Position = new();
        
    [Input(Guid = "C103C06D-BF68-4C5C-967E-DDD939221292")]
    public readonly InputSlot<bool> UseModuloPosition = new();
        
}