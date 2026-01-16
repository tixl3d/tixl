#nullable enable

using T3.Core.Utils;

namespace Lib.numbers.data.utils;

[Guid("e623b242-98e8-4ac1-9b9a-5e0b98acf088")]
public sealed class SelectVec3FromDict : Instance<SelectVec3FromDict>
                                       , ICustomDropdownHolder,
                                         IStatusProvider
{
    [Output(Guid = "7C93DD8B-2630-48E1-A6C5-C67495A68A5D")]
    public readonly Slot<Vector3> Result = new();

    public SelectVec3FromDict()
    {
        Result.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        _dict = DictionaryInput.GetValue(context);
        if (_dict == null)
            return;

        var keyX = SelectX.GetValue(context);
        var needsUpdate = keyX != _lastKeyForX || _dict.Count != _lastDictCount;

        if (needsUpdate)
        {
            _lastKeyForX = keyX;
            _lastDictCount = _dict.Count;
            _keysValid = StringUtils.TryUpdateVectorKeysRelatedToX(_dict, SelectX.GetValue(context), ref _vectorKeys, 3);
            if (!_keysValid)
            {
                _lastErrorMessage = "Can't find vector3 values in OSC dict";
                return;
            }
        }

        if (!_keysValid)
            return;

        if (_vectorKeys.Count != 3)
        {
            Log.Error("Invalid key computation", this); // sanity check
            return;
        }

        _lastErrorMessage = string.Empty;

        if (_dict.TryGetValue(_vectorKeys[0], out var x)
            && _dict.TryGetValue(_vectorKeys[1], out var y)
            && _dict.TryGetValue(_vectorKeys[2], out var z))
        {
            Result.Value = new Vector3(x, y, z);
        }
    }

    private Dict<float>? _dict; // need to cache for select auto-completion

    private int _lastDictCount;
    private string? _lastKeyForX = string.Empty;
    private bool _keysValid;
    private List<string> _vectorKeys = [];



    #region select dropdown and status provide
    string ICustomDropdownHolder.GetValueForInput(Guid inputId)
    {
        return SelectX.Value ?? string.Empty;
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

    void ICustomDropdownHolder.HandleResultForInput(Guid inputId, string? selected, bool isAListItem)
    {
        SelectX.SetTypedInputValue(selected);
    }

    
    private string _lastErrorMessage = string.Empty;
    public IStatusProvider.StatusLevel GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    public string GetStatusMessage() => _lastErrorMessage;
    #endregion
    

    [Input(Guid = "428006e1-4188-4cc0-81f8-b27867f623a5")]
    public readonly InputSlot<Dict<float>> DictionaryInput = new();

    [Input(Guid = "bd4f9dae-3df8-4777-ae75-8d0b7d6a57d5")]
    public readonly InputSlot<string?> SelectX = new();

}