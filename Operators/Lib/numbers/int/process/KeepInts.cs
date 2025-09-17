using T3.Core.Utils;

namespace Lib.numbers.@int.process;

[Guid("209aa9d9-0ba8-42da-8a6a-5ff9fdf09f0c")]
internal sealed class KeepInts : Instance<KeepInts>
{
    [Output(Guid = "89EA9253-5AA3-440C-B15B-54B195B7435F")]
    public readonly Slot<List<int>> Result = new(new List<int>(20));

    public KeepInts()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var addValueToList = AddValueToList.GetValue(context);
        var length = BufferLength.GetValue(context).Clamp(1, 100000);
        var newValue = Value.GetValue(context);

        var reset = Reset.GetValue(context);
            
        if(reset)
            _list.Clear();
            
        try
        {
            if (_list.Count != length)
            {
                while (_list.Count < length)
                {
                    _list.Add(0);
                }
            }

            if (addValueToList)
                _list.Insert(0, newValue);
                
            if (_list.Count > length)
            {
                _list.RemoveRange(length, _list.Count - length);
            }

            Result.Value = _list;
        }
        catch (Exception e)
        {
            Log.Warning("Failed to generate list:" + e.Message);
        }

    }

    private readonly List<int> _list = [];
        
    [Input(Guid = "F9598F16-8FDB-40CA-809A-13AF7B818AD9")]
    public readonly InputSlot<int> Value = new();
        
    [Input(Guid = "e32048ba-449e-48fe-891b-8c16e9d8172c")]
    public readonly InputSlot<bool> AddValueToList = new();
        
    [Input(Guid = "65e2ef46-fe76-47a8-9c61-58db4992230a")]
    public readonly InputSlot<int> BufferLength = new();

    [Input(Guid = "2773b599-cb2d-43e5-a1d7-726772222b04")]
    public readonly InputSlot<bool> Reset = new();

        
}