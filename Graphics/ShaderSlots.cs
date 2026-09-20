namespace T3.Graphics;

/// <summary>
/// Where each register class and each shader stage lands in a shader's binding numbers. One table, because
/// the shader compiler, the compatibility layer and the backends all have to agree on it: the compiler emits
/// these numbers, the compatibility layer computes them from a D3D11 stage and slot, and the backend resolves
/// them back.
/// </summary>
/// <remarks>
/// D3D11 gives every stage its own slot space, so a vertex shader's t0 and a pixel shader's t0 are different
/// resources. Vulkan has one space per descriptor set, and a separate set per stage is not reachable from
/// TiXL's shaders — Slang derives the set from the HLSL register space, which none of them declare. Offsetting
/// each stage's bindings inside one set is, through the compiler's register shifts, and it keeps the stages
/// apart just as well.
/// </remarks>
public static class ShaderSlots
{
    /// <summary>Register class offsets inside a stage: s, b, t, u — the shifts the shaders are compiled with.</summary>
    public const int SamplerBase = 0;

    public const int ConstantBufferBase = 16;
    public const int ShaderResourceBase = 32;
    public const int UnorderedAccessBase = 160;

    /// <summary>Room for one stage's registers; the D3D11 limits fit into it with a little slack.</summary>
    public const int StageStride = 200;

    public static int BaseOf(ShaderStage stage)
    {
        return stage switch
                   {
                       ShaderStage.Vertex   => 0,
                       ShaderStage.Pixel    => StageStride,
                       ShaderStage.Geometry => StageStride * 2,
                       _                    => StageStride * 3,
                   };
    }
}
