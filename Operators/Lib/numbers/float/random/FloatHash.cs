namespace Lib.numbers.@float.random;

[Guid("f2b80aaa-b353-45cc-9504-48c86e69ce99")]
internal sealed class FloatHash : Instance<FloatHash>
{
    [Output(Guid = "4BBBD40F-3D03-4AB7-B7CE-664578EFED94")]
    public readonly Slot<int> Result = new();

    public FloatHash()
    {
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var floatSeed = Seed.GetValue(context);
        var intSeed = HashRawBitsToInt32(floatSeed);
        var makeUniqueForChild = UniqueForChild.GetValue(context);
        
        
            
        var childId = SymbolChildId;
        var bigInteger= new BigInteger(childId.ToByteArray());
        var childSeed = makeUniqueForChild 
                            ? (uint)(bigInteger & 0xFFFFFFFF) 
                            : 0;
            
        var randomValue = PcgHash((uint)(childSeed + intSeed));
        Result.Value = (int)randomValue;
    }
    

    // PCG output function variant (Output XSL RR) - as provided before
    // This remains the core integer hashing logic.
    private static uint PcgHash(uint input)
    {
        uint state = input * 747796405u + 2891336453u; // LCG step
        uint word = ((state >> (int)((state >> 28) + 4u)) ^ state) * 277803737u; // Permutation
        return (word >> 22) ^ word; // Output transformation
    }
    
    /// <summary>
    /// Computes a 32-bit signed hash for a float value by directly hashing its raw bits.
    /// WARNING: Directly uses the float's binary representation. This means:
    /// - +0.0f and -0.0f will produce DIFFERENT hashes.
    /// - Different NaN representations might produce DIFFERENT hashes.
    /// - Infinity values will be hashed based on their specific bit patterns.
    /// This method is fast but ignores standard float equality rules.
    /// </summary>
    /// <param name="input">The float value to hash.</param>
    /// <returns>An int32 hash value based on the float's raw bit pattern.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // Suggest inlining for performance
    public static int HashRawBitsToInt32(float input)
    {
        // 1. Reinterpret the float bits directly as a uint using Unsafe.As
        uint bits = Unsafe.As<float, uint>(ref input);

        // 2. Apply the PCG hash function to the raw bits and cast to int
        return (int)PcgHash(bits);
    }    
    

    [Input(Guid = "9427B1CB-DA57-431F-B182-DE16B7E11002")]
    public readonly InputSlot<float> Seed = new();
    
    [Input(Guid = "dbeffbd5-92b0-4a63-9d28-5592c6b42b69")]
    public readonly InputSlot<bool> UniqueForChild = new();
}