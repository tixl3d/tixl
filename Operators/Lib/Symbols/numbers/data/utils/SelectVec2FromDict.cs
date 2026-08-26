namespace Lib.numbers.data.utils;


[Guid("96b1e8f3-0b42-4a01-b82b-44ccbd857400")]
public sealed class SelectVec2FromDict : Instance<SelectVec2FromDict>, ICustomDropdownHolder
{
    [Output(Guid = "7FF4E818-695D-4FFF-AA23-544D623EADFE")]
    public readonly Slot<Vector2> Result = new();

        
    public SelectVec2FromDict()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _dict = DictionaryInput.GetValue(context);
        _selectCommand = SelectX.GetValue(context);

        if (_dict == null)
            return;

        if (_yKey == null)
            _yKey = FindKeyForY(_selectCommand);

        if (_yKey != null && _dict.TryGetValue(_selectCommand, out var x) && _dict.TryGetValue(_yKey, out var y))
        {
            Result.Value = new Vector2(x, y);
        }
    }

    private Dict<float> _dict;
    private string _selectCommand;

    private string _yKey;
        
    #region select dropdown
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return SelectX.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != SelectX.Id || _dict == null)
        {
            yield return "";
            yield break;
        }

        foreach (var key in _dict.Keys)
        {
            yield return key;
        }
    }

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string selected, bool isAListItem)
    {
        SelectX.SetTypedInputValue(selected);
        _yKey= FindKeyForY(selected);
    }

    private string FindKeyForY( string xKey)
    {
        if (_dict == null)
            return null;
            
        var justFoundX = false;
        foreach (var key in _dict.Keys.OrderBy(x => x))
        {
            if (key == xKey)
            {
                justFoundX = true;
                continue;
            }

            if (justFoundX)
            {
                return key;
            }
        }

        return null;
    }
        
    #endregion        
        
        
        
    [Input(Guid = "2f3472cb-4df8-41e7-b20e-1166992bf732")]
    public readonly InputSlot<Dict<float>> DictionaryInput = new();

    [Input(Guid = "c3f9db97-0355-4da7-9571-b806e9f2a191")]
    public readonly InputSlot<string> SelectX = new();
}