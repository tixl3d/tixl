namespace T3.Editor.Gui.Interaction.Midi.CommandProcessing;

/// <summary>
/// Describes either a single or range of buttons on a midi device  
/// </summary>
public readonly struct ButtonRange
{
    public ButtonRange(int startIndex)
    {
        _startIndex = startIndex;
        _lastIndex = startIndex;
        _reversed = true;
        _mapToIndex = null;
    }

    public override string ToString()
    {
        return _startIndex == _lastIndex 
                   ? $"[{_startIndex}]" 
                   : $"[{_startIndex}..{_lastIndex}] {(_reversed ? " reversed" : "")}";
    }

    /// <param name="mapToIndex">
    /// Optional remap from the button's position within the range (0..count-1) to the snapshot
    /// activation index it controls. Lets a device translate its physical pad numbering to the
    /// canonical 0-based, top-left-first order (e.g. the APC Mini flips its bottom-up rows). Applied
    /// to both LED output and button input, so the two stay in sync.
    /// </param>
    public ButtonRange(int startIndex, int lastIndex, bool reversed = false, Func<int, int> mapToIndex = null)
    {
        _startIndex = startIndex;
        _lastIndex = lastIndex;
        _reversed = reversed;
        _mapToIndex = mapToIndex;
    }

    public bool IncludesButtonIndex(int index)
    {
        return index >= _startIndex && index <= _lastIndex;
    }

    public int GetMappedIndex(int buttonIndex)
    {
        var position = _reversed
                           ? _lastIndex - (buttonIndex - _startIndex)
                           : buttonIndex - _startIndex;
        return _mapToIndex != null ? _mapToIndex(position) : position;
    }

    public IEnumerable<int> Indices()
    {
        for (var index = _startIndex; index <= _lastIndex; index++)
        {
            yield return _reversed 
                             ? (_lastIndex-_startIndex) 
                             :   index;
        }
    }

    public IEnumerable<int> GetMatchingIndicesFromSignals(List<ButtonSignal> signals)
    {
        if(signals.Count == 0)
            return new List<int>();

        var matches = new List<int>();
        foreach (var s in signals)
        {
            if(IncludesButtonIndex(s.ButtonId))
                matches.Add(GetMappedIndex(s.ButtonId));
        }

        return matches;
    }



    public bool IsRange => _lastIndex > _startIndex;
    
    public int StartIndex => _startIndex;
    public int LastIndex => _lastIndex;

    private readonly int _startIndex;
    private readonly int _lastIndex;
    private readonly bool _reversed;
    private readonly Func<int, int> _mapToIndex;
}