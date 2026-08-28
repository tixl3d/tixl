using T3.Core.Utils;


namespace Lib.@string.combine;

[Guid("98bd1491-6e69-4ae0-9fc1-0be8e6a72d32")]
internal sealed class BlendStrings : Instance<BlendStrings>
{
    [Output(Guid = "1bb629bb-dd30-48df-b6b4-3245af10dc09")]
    public readonly Slot<string> Result = new();

    public BlendStrings()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var needsUpdate = false;
        
        var stringInputs = InputStrings.GetCollectedTypedInputs();
        var stringCount = stringInputs.Count;

        needsUpdate |= Blend.GetChangedValue(context, ref _blend, out var floatIndex);
        
        floatIndex = floatIndex.Clamp(0, stringCount - 1.0001f);
        var index = (int)floatIndex;

        string strA;
        string strB;
                
        InputStrings.DirtyFlag.Clear();
        if (stringCount == 0)
        {
            return;
        }

        if (stringCount == 1)
        {
            needsUpdate |= stringInputs[0].GetChangedValue(context, ref _strA, out strA);
            strB = strA;
            //strB = strA = stringInputs[0].GetValue(context);
        }
        else
        {
            needsUpdate |= stringInputs[index].GetChangedValue(context, ref _strA, out strA);
            needsUpdate |= stringInputs[index+1].GetChangedValue(context, ref _strB, out strB);
            // strA = stringInputs[index].GetValue(context);
            // strB = stringInputs[index+1].GetValue(context);
        }
        
        var blendIndex = floatIndex - index;
        
        strA ??= string.Empty;
        strB ??= string.Empty;

        if (strA == string.Empty && strB == string.Empty)
        {
            Result.Value = string.Empty;
            return;
        }

        BlendSpread.GetChangedValue(context, ref _blendSpread, out var blendSpread);
        //var blendSpread = BlendSpread.GetValue(context);

        var totalMaxLength = MaxLength.GetValue(context).Clamp(1, 10000); // 10000 is going to be slow!
        var maxLength = Math.Max(strA.Length, strB.Length).Clamp(1,totalMaxLength);

        needsUpdate|=Scramble.GetChangedValue(context, ref _scrambleFactor, out var scrambleFactor);
        needsUpdate|=ScrambleSeed.GetChangedValue(context, ref _scrambleSeed, out var scrambleSeed);
        
        //var scrambleFactor = Scramble.GetValue(context);
        //var scrambleSeed = ScrambleSeed.GetValue(context);

        needsUpdate|=Characters.GetChangedValue(context, ref _chars, out var chars);
        
        //var chars = Characters.GetValue(context);
        if (string.IsNullOrEmpty(chars))
            chars = FallbackChars;

        if (!needsUpdate)
            return;
            
        
        _stringBuilder.Clear();

        //var needsUpdate = strA != _strA || strB != _strB || chars != _chars;

        for (int charIndex = 0; charIndex < maxLength; charIndex++)
        {
            var charA = GetCharOrSpace(strA, charIndex);
            var charB = GetCharOrSpace(strB, charIndex);
                
            if (charA == '\n' || charB == '\n')
            {
                _stringBuilder.Append(charA);
                continue;
            }

            var charCount = chars.Length;
            var charAInt = chars.IndexOf(charA).Clamp(0,charCount-1);
            var charBInt = chars.IndexOf(charB).Clamp(0,charCount-1);
                
            var hashA = MathUtils.Hash01((uint)((charIndex * 123 + scrambleSeed/100))); 
            var scrambleOffset = hashA < scrambleFactor 
                                     ? (MathUtils.Hash01((uint)(charIndex * 123 + scrambleSeed )) - 0.5f) * charCount 
                                     : 0;

            var x = maxLength <= 1 ? 0: charIndex/ (float)(maxLength-1);
            var blendProgressForChar = ProgressTransition(x, blendIndex, blendSpread);
            //var blendedValue = (int)(charAInt + (charBInt - charAInt) * blendProgressForChar + scrambleOffset).Clamp(0, charCount-1);
            var blendedValue = (int)(charBInt + (charAInt - charBInt) * blendProgressForChar + scrambleOffset).Clamp(0, charCount-1);
            var s = chars[blendedValue];
            _stringBuilder.Append(s);
        }

        Result.Value = _stringBuilder.ToString();
    }

    // private static bool GetValue<T>(in InputSlot<T> input, in EvaluationContext context,  ref T oldValue, out T newValue)
    // {
    //     newValue = input.GetValue(context);
    //     return newValue != oldValue;
    // }

    private string _strA, _strB, _chars; 
    
    private float _blend;
    private float _blendSpread;
    
    private readonly StringBuilder _stringBuilder = new();


    private static char GetCharOrSpace(string str, int index)
    {
        if (index < 0 || index >= str.Length)
            return ' ';
            
        return str[index];
    }
        
    /// <summary>
    /// Return a normalized progress value for a given t and spreading.
    /// </summary>
    /// <remarks>
    /// This is easier visualized than explained. Please have a look at: https://www.desmos.com/calculator/vd2njtavqq
    /// </remarks>
    public static float ProgressTransition(float x, float progress, float spread=1)
    {
        return ((x-progress)/spread - progress + 1).Clamp(0,1);    
    }
        
        
        
    private const string FallbackChars = " .-/\\?#<^*()&AÁÄBCDEFGHIJKLMNOÄÓÖPQRSTUÜÜÚVWXYZaäåbcdefghijklmnoóöpqrsßtuúüvwxyz0123456789";

    [Input(Guid = "EB2D968D-0CE0-48F1-A014-29475A3F0B04")]
    public readonly MultiInputSlot<string> InputStrings = new();
    

        
    [Input(Guid = "2EFD4A0C-958C-49F6-86CB-F8D9FD6FB308")]
    public readonly InputSlot<float> Blend= new();
        
    [Input(Guid = "C3E0CDE4-FECF-4802-A287-A173A6A12518")]
    public readonly InputSlot<float> BlendSpread= new();
        
    [Input(Guid = "DC4E5B79-53E5-463A-92AD-D9BB1F2B0495")]
    public readonly InputSlot<float> Scramble= new();

    [Input(Guid = "D95E112F-B89A-4AA1-954B-10521C0A3815")]
    public readonly InputSlot<int> ScrambleSeed= new();

    [Input(Guid = "D70DA276-C047-42DB-A921-5C1263613CBB")]
    public readonly InputSlot<int> MaxLength= new();

    
    [Input(Guid = "48DE52F6-9B8D-4C12-9336-40091824BE43")]
    public readonly InputSlot<string> Characters= new();

    [Input(Guid = "3197934e-d0ed-4a81-9dc1-2cc63d97ac6f")]
    public readonly InputSlot<string> InputTextA = new();

    [Input(Guid = "CCC21ECC-2877-4FE7-8D78-F7E2A708D762")]
    public readonly InputSlot<string> InputTextB= new();

    private float _scrambleFactor;
    private int _scrambleSeed;
}