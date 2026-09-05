namespace Lib.numbers.data.utils;


[Guid("fd5467c7-c75d-4755-8885-fd1ff1f07c95")]
public sealed class SelectFloatFromDict : Instance<SelectFloatFromDict>, IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "4b281a08-46e9-4036-9a80-29caf11e3b6c")]
    public readonly Slot<float> Result = new(0f);



    public SelectFloatFromDict() : base()
    {
        Result.Value = 0f;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _dict = DictionaryInput.GetValue(context);
        _selectCommand = Select.GetValue(context);
        if (_dict != null)
            _dict.TryGetValue(_selectCommand, out Result.Value);
    }

    private Dict<float> _dict;
    private string _selectCommand;
        
        
    #region implement status provider
    private void SetStatus(string message, IStatusProvider.StatusLevel level)
    {
        _lastWarningMessage = message;
        _statusLevel = level;
    }
        
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
        

    public IStatusProvider.StatusLevel GetStatusLevel() => _statusLevel;
    public string GetStatusMessage() => _lastWarningMessage;

    private string _lastWarningMessage = "Not updated yet.";
    private IStatusProvider.StatusLevel _statusLevel;
    #endregion
        
        
    [Input(Guid = "126D52EB-CDF9-48E6-AC77-BB6E90700C56")]
    public readonly InputSlot<Dict<float>> DictionaryInput = new();

    [Input(Guid = "B0ACB8AD-9F90-4908-B780-1297E0A1D572")]
    public readonly InputSlot<string> Select = new();
}