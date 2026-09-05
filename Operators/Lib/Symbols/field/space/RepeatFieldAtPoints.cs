namespace Lib.field.space;

/// <summary>
/// This operator only contains the shader setup to prepare a structuredBuffer with transform matrices
/// for each point (e.g. slightly improving the ray marching performance). See <see cref="ExecuteRepeatFieldAtPoints"/>
/// for the actual implementation of the IShaderGraph note.
/// </summary>
[Guid("e77ac861-5003-4899-a5e0-83059cdde88d")]
internal sealed class RepeatFieldAtPoints : Instance<RepeatFieldAtPoints>
{
    [Output(Guid = "202481ea-bf94-4fd3-ad2c-84dbf7622dea")]
    public readonly Slot<ShaderGraphNode> Result = new Slot<ShaderGraphNode>();

    [Input(Guid = "df588d92-76bb-407f-9042-93ddf12e8394")]
    public readonly InputSlot<ShaderGraphNode> InputField = new InputSlot<ShaderGraphNode>();

    [Input(Guid = "9a7f3066-de71-4729-bc9e-5db0d8fd9eaa")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

    private enum CombineMethods
    {
        Union,
        UnionSoft,
        UnionRound,
    }
    
        [Input(Guid = "08ad7bce-1161-4a43-997e-fea2e792ae36")]
        public readonly InputSlot<float> K = new InputSlot<float>();

        [Input(Guid = "26190514-6762-4322-87e2-393670a476a6", MappedType = typeof(CombineMethods))]
        public readonly InputSlot<int> CombineMethod = new InputSlot<int>();
}