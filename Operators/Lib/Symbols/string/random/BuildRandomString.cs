using T3.Core.Utils;

namespace Lib.@string.random;

[Guid("7b21f10b-3548-4a23-95df-360addaeb03d")]
internal sealed class BuildRandomString : Instance<BuildRandomString>
{
    [Output(Guid = "ABA9EB42-5AF0-4165-A2BD-FDFCD4340484", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<string> Result = new();

    [Output(Guid = "8116d50e-0220-4bb7-b09d-881f722804cd")]
    public readonly Slot<StringBuilder> Builder = new();

    public BuildRandomString()
    {
        Result.UpdateAction += Update;
        Builder.UpdateAction += Update;
    }

    private double _lastUpdateTime = 0;

    private void Update(EvaluationContext context)
    {
        var maxLength = MaxLength.GetValue(context);
        var stringBuilder = OverrideBuilder.GetValue(context);

        if (Result.Value != null &&  Math.Abs(context.LocalFxTime - _lastUpdateTime) < 0.001)
            return;

        _lastUpdateTime = context.LocalFxTime;
        var scrambleSeed = ScrambleSeed.GetValue(context);
        //var lastIndex = _index;

        if (maxLength <= 0)
        {
            Result.Value= String.Empty;
            Result.Value= string.Empty;
            return;
        }
            
        if (!OverrideBuilder.HasInputConnections || stringBuilder == null)
        {
            stringBuilder = _fallbackBuffer;
        }
            
        if (Clear.GetValue(context))
        {
            stringBuilder.Clear();
            _index = 0;
        }

        //var mode = (Modes)WriteMode.GetValue(context);

        try
        {
            var scrambleRatio = ScrambleRatio.GetValue(context);

            var scrambleEnabled = Scramble.GetValue(context);
            if (scrambleRatio > 0 && scrambleEnabled)
            {
                for (int index = 0; index < stringBuilder.Length; index++)
                {
                    var hash = (float)((double)MathUtils.XxHash((uint)index + (uint)scrambleSeed * 123127) / uint.MaxValue);
                    if (hash < scrambleRatio)
                    {
                        var scrambleChunkEnd = index + hash * stringBuilder.Length + 1;
                        while (index < stringBuilder.Length && index < scrambleChunkEnd)
                        {
                            var c = stringBuilder[index];
                            if (c != '\n')
                            {
                                if (c == 32)
                                    c = (char)90;

                                c= (char)(c-1);
                                stringBuilder[index] = c;
                            }
                            index++;
                        }
                    }
                }
            }
                
            if (Insert.GetValue(context))
            {
                if (JumpToRandomPos.GetValue(context))
                    _index = (int)_random.NextLong(0, stringBuilder.Length);

                var separator = Separator.GetValue(context); ;
                if (!string.IsNullOrEmpty(separator))
                    separator = separator.Replace("\\n", "\n");

                var str = InsertString.GetValue(context);
                var insertString = str + separator;
                var insertLength = insertString.Length;
                var currentLength = stringBuilder.Length;
                var lineWrap = (WrapLinesModes)WrapLines.GetValue(context);
                var mode = (Modes)WriteMode.GetValue(context);
                    
                if (_index > maxLength)
                    _index = 0;
                    
                var pos = _index;
                if (pos + insertLength > maxLength)
                {
                    insertLength = maxLength - pos;
                }

                if (mode != Modes.Insert && pos < currentLength - insertLength)
                {
                    stringBuilder.Remove(pos, insertLength);
                }

                if (pos > currentLength)
                {
                    stringBuilder.Append(new string(' ', pos - currentLength));
                }
                    
                stringBuilder.Insert(pos, insertString);
                    
                InsertLineWraps(lineWrap, stringBuilder, pos, insertLength, WrapLineColumn.GetValue(context).Clamp(1,1000));

                    
                    
                switch (mode)
                {
                    case Modes.Insert:
                    {
                        _index += insertLength;
                        _index %= maxLength;
                        break;
                    }
                    case Modes.Overwrite:
                    {
                        _index += insertLength;
                        _index %= maxLength;
                        break;
                    }
                    case Modes.OverwriteAtFixedOffset:
                        _index += OverwriteOffset.GetValue(context);
                        if (_index > maxLength)
                        {
                            _index = _index % maxLength;
                        }
                        else if (_index < 0)
                        {
                            _index += maxLength - insertLength;
                        }
                            
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
                    
                    
                //InsertLineWraps(lineWrap, stringBuilder);
                    
                if (stringBuilder.Length > maxLength)
                    stringBuilder.Length = maxLength;
                    
            }
                
                
        }
        catch (Exception e)
        {
            Log.Warning($"Failed to manipulate string at index {_index} " + e.Message);
        }

        //

        Builder.Value = stringBuilder;
        Result.Value = stringBuilder.ToString();
    }

    private void InsertLineWraps(WrapLinesModes lineWrap, StringBuilder stringBuilder, int insertPos, int insertLength, int wrapColumn)
    {
        if (lineWrap == WrapLinesModes.WrapAtCharacters)
        {
            var lookBackIndex = insertPos;
            while (lookBackIndex > 0 && stringBuilder[lookBackIndex] != '\n')
            {
                lookBackIndex--;
            }

            var lineLength = insertPos - lookBackIndex + insertLength;
            if (lineLength > wrapColumn && insertPos > 0 && insertPos < stringBuilder.Length)
            {
                stringBuilder[insertPos - 1] = '\n';
                return;
            }
                
            var lookForwardIndex = insertPos;
            while (lookForwardIndex < stringBuilder.Length && stringBuilder[lookForwardIndex] != '\n')
            {
                lookForwardIndex++;
            }

            if (lookForwardIndex - lookBackIndex > wrapColumn)
            {
                stringBuilder[insertPos + insertLength - 1] = '\n';
            }
        }
        else if (lineWrap == WrapLinesModes.WrapAtWords)
        {
            int pos = 0;
            int currentLineLength = 0;
            int lastValidBreakPos = -1;
            while (pos < stringBuilder.Length)
            {
                var c = stringBuilder[pos];
                if (c == '\n') 
                {
                    currentLineLength = 0;
                    lastValidBreakPos = -1;
                }
                    
                else if (c == ' ' || c == '.' || c == ',' || c == '/')
                {
                    lastValidBreakPos = pos;
                    currentLineLength++;
                }
                else
                {
                    currentLineLength++;
                }

                if (currentLineLength > wrapColumn && lastValidBreakPos != -1)
                {
                    stringBuilder[lastValidBreakPos] = '\n';
                    pos = lastValidBreakPos;
                    lastValidBreakPos = -1;
                    currentLineLength = 0;
                }
                pos++;
            }
        }
        else if (lineWrap == WrapLinesModes.WrapToFillBlock)
        {
            int pos = 0;
            int currentLineLength = 0;
            int lastValidBreakPos = -1;
                
            while (pos < stringBuilder.Length)
            {
                var c = stringBuilder[pos];
                if (c == '\n')
                {
                    stringBuilder[pos] = ' ';
                    //currentLineLength = 0;
                    lastValidBreakPos = pos;
                    currentLineLength++;
                }
                    
                else if (c == ' ' || c == '.' || c == ',' || c == '/')
                {
                    lastValidBreakPos = pos;
                    currentLineLength++;
                }
                else
                {
                    currentLineLength++;
                }

                if (currentLineLength > wrapColumn && lastValidBreakPos != -1)
                {
                    stringBuilder[lastValidBreakPos] = '\n';
                    pos = lastValidBreakPos;
                    lastValidBreakPos = -1;
                    currentLineLength = 0;
                }
                pos++;
            }
        }            
        else if (lineWrap == WrapLinesModes.SolidBlock)
        {
            int pos = 0;
            int currentLineLength = 0;
            while (pos < stringBuilder.Length)
            {
                var c = stringBuilder[pos];
                    
                if (c == '\n')
                {
                    stringBuilder.Remove(pos, 1);
                    continue;
                }
                    
                currentLineLength++;
                pos++;

                if (currentLineLength == wrapColumn)
                {
                    stringBuilder.Insert(pos, '\n');
                    pos++;
                    currentLineLength = 0;
                }
            }
        }
    }

    private enum Modes
    {
        Insert,
        Overwrite,
        OverwriteAtFixedOffset,
    }

    private enum WrapLinesModes
    {
        DontWrap,
        WrapAtWords,
        WrapAtCharacters,
        WrapToFillBlock,
        SolidBlock,
    }

    private StringBuilder _fallbackBuffer = new();

    private int _index = 0;
    private Random _random = new();

    [Input(Guid = "CE436E27-05A5-431D-9AA2-920DBFF639A7", MappedType = typeof(Modes))]
    public readonly InputSlot<int> WriteMode = new();
        
    [Input(Guid = "77A5604A-034A-4352-BD46-BE3CB57F90B7")]
    public readonly InputSlot<bool> Clear = new();
        
    [Input(Guid = "095202BF-118F-4C4C-802E-7916BC290A60")]
    public readonly InputSlot<bool> Insert = new();



    [Input(Guid = "F977FAAF-1840-4A75-9BC5-43176F2E88E9")]
    public readonly InputSlot<bool> JumpToRandomPos = new();



    [Input(Guid = "960179BD-286F-4629-BBCB-CD31AA9C9AE2")]
    public readonly InputSlot<string> InsertString = new();

    [Input(Guid = "8EEE8067-1A4E-4372-93D0-2DBC368AA45A")]
    public readonly InputSlot<string> Separator = new();

    [Input(Guid = "1559C0E9-BA56-447F-8241-03D8D59AC205")]
    public readonly InputSlot<int> OverwriteOffset = new();

    [Input(Guid = "38CE7F47-C117-47A2-AEEA-609716C60555")]
    public readonly InputSlot<int> MaxLength = new();

    [Input(Guid = "875CBFA9-FFA8-4204-810C-C04F5F421441", MappedType = typeof(WrapLinesModes))]
    public readonly InputSlot<int> WrapLines = new();

    [Input(Guid = "BD941C4B-18A8-4687-85A8-3FE53B4F6213")]
    public readonly InputSlot<int> WrapLineColumn = new();

    [Input(Guid = "7DABD7C8-5C2B-4BE2-B1B6-BF8B8FCBFD8D")]
    public readonly InputSlot<float> ScrambleRatio = new();
        
    [Input(Guid = "9253B148-325B-427A-819E-1AE1B1019ADE")]
    public readonly InputSlot<bool> Scramble = new();

    [Input(Guid = "3EA4F12E-7184-45BC-A523-EF6A2E1C5C3D")]
    public readonly InputSlot<int> ScrambleSeed = new();        
        
        
    [Input(Guid = "CCFAC8A9-0954-4869-A47C-B66C714F6545")]
    public readonly InputSlot<StringBuilder> OverrideBuilder = new();
}