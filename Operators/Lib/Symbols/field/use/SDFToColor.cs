namespace Lib.field.use;

/// <summary>
/// This operator only contains the shader setup to prepare a structuredBuffer with transform matrices
/// for each point (e.g. slightly improving the ray marching performance). See <see cref="ExecuteRepeatFieldAtPoints"/>
/// for the actual implementation of the IShaderGraph note.
/// </summary>
[Guid("15a1da54-2869-4d98-a61c-aa9fe89b0125")]
internal sealed class SDFToColor : Instance<SDFToColor>
{
    [Output(Guid = "af7718e9-470c-4294-b44b-2dff008abbdc")]
    public readonly Slot<ShaderGraphNode> Result = new Slot<ShaderGraphNode>();

    [Input(Guid = "a83e4d64-6194-4cfc-b740-55875de56b2f")]
    public readonly InputSlot<ShaderGraphNode> InputField = new InputSlot<ShaderGraphNode>();

    
    
    private enum MappingModes
    {
        Centered,
        FromStart,
        PingPong,
        Repeat,
    }
    
    [Input(Guid = "f3bc8b96-37b8-4be9-9a7c-61acb3e151f9")]
    public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

    [Input(Guid = "b03a6bb2-fb06-4a77-94d9-9924c77e6270", MappedType = typeof(MappingModes))]
    public readonly InputSlot<int> Mapping = new InputSlot<int>();

    [Input(Guid = "be783e46-ca50-4e9d-9e81-12029c5a3265")]
    public readonly InputSlot<float> Range = new InputSlot<float>();

    [Input(Guid = "da4a52ab-8bdd-48f8-b530-ff9e6e303b5d")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "8b5f65ba-4b18-49eb-803d-28e028cf67f7")]
    public readonly InputSlot<System.Numerics.Vector2> GainAndBias = new InputSlot<System.Numerics.Vector2>();
}