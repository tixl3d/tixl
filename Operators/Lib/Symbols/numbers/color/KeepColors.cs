using T3.Core.Utils;

namespace Lib.numbers.color;

[Guid("2ccc7c43-5a5f-4b4e-aca7-078a5121bdb3")]
internal sealed class KeepColors : Instance<KeepColors>
{
    [Output(Guid = "50CC34FE-9991-4690-9894-DFAED758D325")]
    public readonly Slot<List<Vector4>> Result = new(new List<Vector4>(20));

    public KeepColors()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var addColor = AddColorToList.GetValue(context);
        var length = MaxLength.GetValue(context).Clamp(1, 100000);
        var newColor = Color.GetValue(context);

        var reset = Reset.GetValue(context);
            
        if(reset)
            _list.Clear();
            
        try
        {
            if (addColor)
                _list.Insert(0, newColor);
                
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

    private readonly List<Vector4> _list = [];
        
    [Input(Guid = "BE2D418B-CE5E-4545-ABC0-4E56B5665A75")]
    public readonly InputSlot<Vector4> Color = new();
        
    [Input(Guid = "8308d81b-3d9a-40a5-a2a3-8fb5eac201cc")]
    public readonly InputSlot<bool> AddColorToList = new();
        
    [Input(Guid = "af59eb80-a803-421b-8a0a-d11e307bc49f")]
    public readonly InputSlot<int> MaxLength = new();

    [Input(Guid = "b2e05ddc-0000-453a-a531-87a6739d0c85")]
    public readonly InputSlot<bool> Reset = new();

        
}