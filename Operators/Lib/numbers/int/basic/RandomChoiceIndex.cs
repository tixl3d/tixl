using T3.Core.Utils;

namespace Lib.numbers.@int.basic;

[Guid("74901305-773d-4246-b962-2478096159b7")]
internal sealed class RandomChoiceIndex : Instance<RandomChoiceIndex>
{
    [Output(Guid = "8509fd5c-8314-4f6e-906f-5bae82dfeace")]
    public readonly Slot<int> Result = new();

    public RandomChoiceIndex()
    {
        Result.UpdateAction += Update;
    }

    /// <summary>
    /// Avoiding duplicates in a deterministic random shuffle index is more complicated
    /// than you might think
    ///
    /// This algorithm uses a fixed length random slice buffer.
    /// If the current random index in outside the current range of the that buffer,
    /// it is initialized with non-repeating numbers. To resolve the duplications
    /// between the end of that buffer and the next one, we walk backwards and flip
    /// new numbers until the double is resolved. 
    /// </summary>
    private void Update(EvaluationContext context)
    {
        var n = Value.GetValue(context);
        var modulo = Mod.GetValue(context);
        if (modulo < 2)
        {
            Result.Value = 0;
            return;
        }

        if (modulo == 2)
        {
            Result.Value = n.Mod(2);
            return;
        }
        
        if (!_initialized 
            || n < _lastBufferIndex 
            || n >= _lastBufferIndex + ModBufferLength
            || modulo != _modulo)
        {
            _modulo = modulo;
            var bufferStartIndex = n - n.Mod(ModBufferLength) - ModBufferLength/4; // counting backwards more unlikely 
            FillBuffer(bufferStartIndex, modulo);
            _lastBufferIndex = bufferStartIndex;
            _initialized = true;
        }

        var rand = _buffer[n.Mod(ModBufferLength)];

        
        Result.Value = rand;
    }

    private int _lastBufferIndex = -1;
    private bool _initialized;
    private int _modulo = -1;

    
    private void FillBuffer(int startIndex, int modulo)
    {
        // Initial value
        var value = MathUtils.XxHash(startIndex).Mod(modulo);
        _buffer[0] = value;

        // Fill the rest of the buffer
        for (var i = 1; i < ModBufferLength; i++)
        {
            // Use modulo - 1 and add 1 to avoid repetition
            var offset = MathUtils.XxHash(startIndex + i).Mod(modulo - 1) + 1;
            value = (value + offset).Mod(modulo);
            _buffer[i] = value;
        }

        // Fix potential collision with the next sequence start
        var expectedNext = MathUtils.XxHash(startIndex + ModBufferLength).Mod(modulo);

        for (var i = ModBufferLength - 1; i > 0; i--)
        {
            if (_buffer[i] != expectedNext)
                break;

            var offset = MathUtils.XxHash(startIndex + i + 13331).Mod(modulo - 1) + 1;
            var newValue = (expectedNext + offset).Mod(modulo);

            _buffer[i] = newValue;
            expectedNext = newValue;
        }
    }
    
    private const int ModBufferLength = 100;    // balance cost of buffer init with memory 
    
    private readonly int[] _buffer = new int[ModBufferLength]; 
    
    [Input(Guid = "b84a35a2-8eb9-45a2-ae68-bfce77fc1616")]
    public readonly InputSlot<int> Value = new();

    [Input(Guid = "009e80fa-7f42-4db6-abd8-91039602bed6")]
    public readonly InputSlot<int> Mod = new();
}