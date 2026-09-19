namespace Examples.user.pixtur.vj.helpers;

[Guid("8fb63c4d-80a8-4023-b55b-7f97bffbee48")]
public class _GetTidalTrigger : Instance<_GetTidalTrigger>
                              , IStatusProvider, ICustomDropdownHolder
{
    [Output(Guid = "a4121952-5c82-4237-8e9f-913b83c6273b", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Note = new(0f);

    [Output(Guid = "084E6671-1932-451B-9DA0-4A474844AD27", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> WasTrigger = new();

    public _GetTidalTrigger()
    {
        Note.UpdateAction = Update;
        WasTrigger.UpdateAction = Update;
    }

    private double _lastUpdateTime;

    private void Update(EvaluationContext context)
    {
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) < 0.001f)
        {
            return;
        }
            
        var logDebug = LogDebug.GetValue(context);
        _lastUpdateTime = context.LocalFxTime;
            
        _dict = DictionaryInput.GetValue(context);
            
        var useNotesForBeats = UseNotesForBeats.GetValue(context);
        var id = Id.GetValue(context);
        var channel = Channel.GetValue(context);
            
        var path = useNotesForBeats ? $"/dirt/play/{id}/{channel}/"
                       : $"/dirt/play/{id}/{channel}/s_midi/";

        if (_dict == null)
        {
            SetStatus("No dictionary input", IStatusProvider.StatusLevel.Warning);
            return;
        }

        WasTrigger.Value = false;
            
        var notePath = path + NoteChannel.GetValue(context);
        var cyclePath = path + CycleChannel.GetValue(context);

        if (FilterNotes.DirtyFlag.IsDirty)
        {
            _noteFilter.Clear();
            var noteFilter = FilterNotes.GetValue(context);
            if (!string.IsNullOrEmpty(noteFilter))
            {
                foreach (var x in noteFilter.Split(","))
                {
                    if(int.TryParse(x.Trim(), out var n))
                    {
                        _noteFilter.Add(n);
                    }
                }
            }
        }
        
        if (
            _dict.TryGetValue(notePath, out var note)
            && _dict.TryGetValue(cyclePath, out var cycle))
        {
            if (IsValidNote(note))
            {
                if (useNotesForBeats)
                {
                    Note.Value = note;
                    WasTrigger.Value = cycle > _lastCycle;
                    _lastCycle = cycle;
                    if (logDebug)
                    {
                        Log.Debug($"found beat {notePath} '{note}'  '{channel}' " ,this);
                    }
                }
                else
                {
                    Note.Value = note;
                    WasTrigger.Value = cycle > _lastCycle;
                    _lastCycle = cycle;
                }
            }
            SetStatus(null, IStatusProvider.StatusLevel.Success);
        }
        
        if (logDebug)
        {
            Log.Debug($"Note: {notePath} {note}   Cycle: {cyclePath} {_lastCycle}", this);
        }

        Note.DirtyFlag.Clear();
        WasTrigger.DirtyFlag.Clear();
    }

    private bool IsValidNote(float note)
    {
        return _noteFilter.Count == 0 || _noteFilter.Contains((int)note);
    }
    
    private float _lastCycle = 0;

    private Dict<float> _dict;
    private readonly HashSet<int> _noteFilter=[];

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

    [Input(Guid = "eddeab43-5d7e-4f42-abb7-7909c9a7212e")]
    public readonly InputSlot<Dict<float>> DictionaryInput = new();

    [Input(Guid = "E594E270-B748-4B96-AB19-9A0D30CDBCCA")]
    public readonly InputSlot<string> NoteChannel = new();

    [Input(Guid = "255ADFB5-CF8B-4DD7-958F-3D810D48760F")]
    public readonly InputSlot<string> FilterNotes = new();
    
    [Input(Guid = "FF5A9352-8BFA-4D73-9992-306AF55213AE")]
    public readonly InputSlot<string> CycleChannel = new();

    [Input(Guid = "fac3cc60-2a58-4106-a258-3798df04455a")]
    public readonly InputSlot<string> Select = new();
        

    [Input(Guid = "FB11C81B-931A-4E3C-9E1C-9A287C2F64A1")]
    public readonly InputSlot<string> Id = new();

    [Input(Guid = "D16A364D-2466-451C-B639-70FBA4C3357A")]
    public readonly InputSlot<string> Channel = new();


    [Input(Guid = "8E355D24-4934-4008-990D-76448A647281")]
    public readonly InputSlot<bool> UseNotesForBeats = new();
        
    [Input(Guid = "183C0A33-A37B-42EB-B160-B370AAD2E924")]
    public readonly InputSlot<bool> LogDebug = new();
}