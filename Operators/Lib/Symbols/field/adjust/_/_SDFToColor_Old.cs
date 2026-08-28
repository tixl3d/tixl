namespace Lib.field.adjust._;

/// <summary>
/// This operator only contains the shader setup to prepare a structuredBuffer with transform matrices
/// for each point (e.g. slightly improving the ray marching performance). See <see cref="ExecuteRepeatFieldAtPoints"/>
/// for the actual implementation of the IShaderGraph note.
/// </summary>
[Guid("eff96322-0e4e-42d6-93de-18451b56ae31")]
internal sealed class _SDFToColor_Old : Instance<_SDFToColor_Old>
{
    [Output(Guid = "5dbdab08-7014-4eda-9000-be7412dfb2eb")]
    public readonly Slot<ShaderGraphNode> Result = new Slot<ShaderGraphNode>();

    [Input(Guid = "422ff7d0-b5b8-441b-baea-03744dc320ad")]
    public readonly InputSlot<ShaderGraphNode> InputField = new InputSlot<ShaderGraphNode>();

    private enum CombineMethods
    {
        Union,
        UnionSoft,
        UnionRound,
    }

        [Input(Guid = "9ddd3ea5-dc29-4430-9c99-746d63c1fb7a")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "8b8950bc-0b94-4db5-869b-51759e1b492e")]
        public readonly InputSlot<System.Numerics.Vector2> Range = new InputSlot<System.Numerics.Vector2>();
}