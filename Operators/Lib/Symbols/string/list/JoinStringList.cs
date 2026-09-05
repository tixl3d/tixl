namespace Lib.@string.list;

[Guid("51943340-70b1-4bb5-8cb9-0e79d366a57b")]
internal sealed class JoinStringList : Instance<JoinStringList>, IStatusProvider
{
    [Output(Guid = "ef105688-3e28-47c3-8b8e-5fda3bde3090")]
    public readonly Slot<string> Result = new();    public JoinStringList()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var separatorValue = Separator.GetValue(context);
        if (separatorValue == null)
        {
            _lastErrorMessage = "Separator can't be null.";
            Result.Value = string.Empty;
            return;
        }
        
        var separator = separatorValue.Replace("\\n", "\n");
        var input = Input.GetValue(context);
        if (input == null || input.Count == 0)
        {
            _lastErrorMessage = "Can't join empty string list.";
            Result.Value = string.Empty;
            return;
        }

        _lastErrorMessage = null;
        Result.Value = string.Join(separator, input);
    }

    [Input(Guid = "DB366216-B485-48BF-B267-56344B317CF7")]
    public readonly InputSlot<List<string>> Input = new ();
        
    [Input(Guid = "89350e3c-2b83-4720-bfa1-d4adc6cc02fa")]
    public readonly InputSlot<string> Separator = new();

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() 
        => string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;

    private string _lastErrorMessage;
}