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
    }

    public override string ToString()
    {
        return _startIndex == _lastIndex 
                   ? $"[{_startIndex}]" 
                   : $"[{_startIndex}..{_lastIndex}] {(_reversed ? " reversed" : "")}";
    }

    public ButtonRange(int startIndex, int lastIndex, bool reversed = false)
    {
        _startIndex = startIndex;
        _lastIndex = lastIndex;
        _reversed = reversed;
    }

    public bool IncludesButtonIndex(int index)
    {
        return index >= _startIndex && index <= _lastIndex;
    }

    public int GetMappedIndex(int buttonIndex)
    {
        return _reversed 
                   ? _lastIndex - (buttonIndex - _startIndex)
                   :buttonIndex - _startIndex;
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
}