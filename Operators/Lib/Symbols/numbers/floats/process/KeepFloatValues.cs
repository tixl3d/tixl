using T3.Core.Utils;

namespace Lib.numbers.floats.process;

[Guid("32a1d28e-23bc-44a8-becc-b52972464526")]
internal sealed class KeepFloatValues : Instance<KeepFloatValues>
{
    [Output(Guid = "62b18ce3-1ffe-475e-9955-6f72c2fe6e18")]
    public readonly Slot<List<float>> Result = new(new List<float>(20));

    public KeepFloatValues()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var addValueToList = AddValueToList.GetValue(context);
        var length = BufferLength.GetValue(context).Clamp(1, 100000);
        var newValue = Value.GetValue(context);
        var defaultValue = DefaultValue.GetValue(context);

        var reset = Reset.GetValue(context);
            
        if(reset)
            _list.Clear();
            
        try
        {
            if (_list.Count != length)
            {
                while (_list.Count < length)
                {
                    _list.Add(defaultValue);
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

    private List<float> _list = new();
        
    [Input(Guid = "A5ED1DB1-2DC9-43B2-97C3-CD416D07089B")]
    public readonly InputSlot<float> Value = new();
        
    [Input(Guid = "C84AA4CF-CE04-43E2-8D88-4CE3B8A7155E")]
    public readonly InputSlot<bool> AddValueToList = new();
        
    [Input(Guid = "519133D3-0440-4C8A-8BC8-75AA17219AD5")]
    public readonly InputSlot<int> BufferLength = new();

    [Input(Guid = "D11E3F87-ECFA-44B9-8672-96562685CDCE")]
    public readonly InputSlot<bool> Reset = new();

    [Input(Guid = "639DC9C2-E3B9-4E17-959E-CB181166B0A2")]
    public readonly InputSlot<float> DefaultValue = new();

        
}