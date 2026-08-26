using T3.Core.Utils;

namespace Lib.@string.list;

[Guid("56eda8f4-09fc-48a3-ab1d-fbff4f4b6438")]
internal sealed class KeepStrings : Instance<KeepStrings>
{
    [Output(Guid = "5e1b1aad-2fe2-49f3-9954-a35dc7b3ec25")]
    public readonly Slot<List<string>> Strings = new();

    [Output(Guid = "9661F810-7B94-4187-A2F0-2B26950A2F2E")]
    public readonly Slot<List<float>> InsertTimes = new();

    [Output(Guid = "30760da6-3ed4-41df-aa76-545bd203ea57")]
    public readonly Slot<int> Count = new();

    public KeepStrings()
    {
        Strings.UpdateAction = Update;
    }

    private bool _clear;
        
    private void Update(EvaluationContext context)
    {
        var maxCount = MaxCount.GetValue(context).Clamp(0, 10000);
        var insertTriggered = InsertTrigger.GetValue(context);
        var index = Index.GetValue(context).Mod(maxCount);

        if (MathUtils.WasTriggered(ClearTrigger.GetValue(context), ref _clear))
        {
            _strings.Clear();
            _insertTimes.Clear();
            ClearTrigger.SetTypedInputValue(false);
        }
            
        var onlyOnChanges = OnlyOnChanges.GetValue(context);
        var newStr = NewString.GetValue(context);
        var hasStringChanged = newStr != _lastString;
        if (hasStringChanged)
        {
            _lastString = newStr;
        }

        var insertMode = InsertMode.GetEnumValue<InsertModes>(context);

        if (insertTriggered)
        {
            switch (insertMode)
            {
                case InsertModes.Append:
                    if (hasStringChanged || !onlyOnChanges)
                    {
                        _strings.Add(newStr);
                        _insertTimes.Add((float)context.LocalFxTime);

                        while (_strings.Count > maxCount && _strings.Count > 1)
                        {
                            _strings.RemoveAt(0);
                            _insertTimes.RemoveAt(0);
                        }

                        _index = _strings.Count - 1;
                    }

                    break;
                case InsertModes.Insert:
                    if (hasStringChanged || !onlyOnChanges)
                    {
                        _strings.Insert(0, newStr);
                        _insertTimes.Insert(0, (float)context.LocalFxTime);

                        while (_strings.Count > maxCount && _strings.Count > 1)
                        {
                            _strings.RemoveAt(_strings.Count - 1);
                            _insertTimes.RemoveAt(_insertTimes.Count - 1);
                        }

                        _index = 0;
                    }
                    break;
                case InsertModes.Overwrite:
                    if (hasStringChanged || !onlyOnChanges)
                    {
                        if (maxCount > 0)
                        {
                            if (_strings.Count == 0)
                            {
                                _strings.Add(newStr);
                                _insertTimes.Add((float)context.LocalFxTime);
                            }
                            else if(_strings.Count < maxCount)
                            {
                                _strings.Add(newStr);
                                _insertTimes.Add((float)context.LocalFxTime);
                                _index = _strings.Count - 1;
                            }
                            else
                            {
                                _index = (_index+1) % _strings.Count;
                                _strings[_index] = newStr;
                                _insertTimes[_index] = (float)context.LocalFxTime;
                            }
                        }

                        while (_strings.Count > maxCount && _strings.Count > 1)
                        {
                            _strings.RemoveAt(_strings.Count - 1);
                            _insertTimes.RemoveAt(_insertTimes.Count - 1);
                        }
                    }
                    break;
                    
                case InsertModes.UseIndex:
                    try
                    {
                        while (_strings.Count > maxCount && _strings.Count > 1)
                        {
                            _strings.RemoveAt(_strings.Count - 1);
                            _insertTimes.RemoveAt(_insertTimes.Count - 1);
                        }
                            
                        if (hasStringChanged || !onlyOnChanges)
                        {
                            if (_strings.Count <= index)
                            {
                                _strings.Add(newStr);
                                _insertTimes.Add((float)context.LocalFxTime);
                            }
                            else
                            {
                                _strings[index] = newStr;
                                _insertTimes[index]= (float)context.LocalFxTime;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Debug($"Error in KeepStrings: {index} {e.Message} {e.StackTrace}", this);
                    }
                    break;
            }
        }

        Strings.Value = _strings;
        InsertTimes.Value = _insertTimes;
        Count.Value = _strings.Count;
    }


    private readonly List<string> _strings = new();
    private readonly List<float> _insertTimes = new();
    private int _index;

    private string _lastString = string.Empty;

    private enum InsertModes
    {
        Append,
        Insert,
        Overwrite,
        UseIndex,
    }

    [Input(Guid = "26ADB8D2-14E7-4006-99ED-BCBBEDE8352A")]
    public readonly InputSlot<string> NewString = new();

    [Input(Guid = "D045F941-7035-4A84-B0B0-2691477EF375")]
    public readonly InputSlot<bool> InsertTrigger = new();

    [Input(Guid = "1B777F1E-E219-4B8F-B185-94466C280881")]
    public readonly InputSlot<int> MaxCount = new();

    [Input(Guid = "2FF583B8-8B3E-4441-AA60-7365F4C54320")]
    public readonly InputSlot<bool> ClearTrigger = new();

    [Input(Guid = "3A9E3E1C-E8C2-46C7-96AC-B7C317205ED4")]
    public readonly InputSlot<bool> OnlyOnChanges = new();

    [Input(Guid = "694574A3-A3A9-4073-BEEC-A6A10ED64B81", MappedType = typeof(InsertModes))]
    public readonly InputSlot<int> InsertMode = new();
        
    [Input(Guid = "ba5cd2ae-15df-4d67-8690-a5b0d1b81971")]
    public readonly InputSlot<int> Index = new(0);
}