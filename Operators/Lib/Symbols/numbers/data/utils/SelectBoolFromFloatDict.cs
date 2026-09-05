namespace Lib.numbers.data.utils;

[Guid("05295c65-7dfd-4570-866e-9b5c4e735569")]
public sealed class SelectBoolFromFloatDict : Instance<SelectBoolFromFloatDict>
                                             ,IStatusProvider,ICustomDropdownHolder
{

    [Output(Guid = "2C0F90BB-E7C8-43A7-A43F-9BAB5222753B")]
    public readonly Slot<bool> Result = new(false);

        
    public SelectBoolFromFloatDict()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _dict = DictionaryInput.GetValue(context);
        _selectCommand = Select.GetValue(context);

        if (_dict != null && _dict.TryGetValue(_selectCommand, out var floatValue))
        {
            Result.Value = floatValue > 0.5f;
            _lastWarningMessage = null;
        }
        else
        {
            _lastWarningMessage = $"Key '{_selectCommand}' not found in dictionary.";
        }
    }

    private Dict<float> _dict;
    private string _selectCommand;
        
        
    #region implement status provider
        
    #region select dropdown
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return Select.Value;
    }

    IEnumerable<string> ICustomDropdownHolder.GetOptionsForInput(Guid inputId)
    {
        if (inputId != Select.Id || _dict == null)
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
        Select.SetTypedInputValue(selected);
    }
    #endregion        
        

    public IStatusProvider.StatusLevel GetStatusLevel() => string.IsNullOrEmpty(_lastWarningMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    public string GetStatusMessage() => _lastWarningMessage;

    private string _lastWarningMessage = "Not updated yet.";
    #endregion
        
        
    [Input(Guid = "e09e3a45-e1de-430f-93ef-9d2412b22cda")]
    public readonly InputSlot<Dict<float>> DictionaryInput = new();

    [Input(Guid = "132db9de-cffe-4379-af85-2aca18b4f6e0")]
    public readonly InputSlot<string> Select = new();
}