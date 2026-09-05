namespace Lib.field.generate.sdf;

/// <summary>
/// This operator only contains the shader setup to prepare a structuredBuffer with transform matrices
/// for each point (e.g. slightly improving the ray marching performance). See <see cref="ExecuteRepeatFieldAtPoints"/>
/// for the actual implementation of the IShaderGraph note.
/// </summary>
[Guid("f8593fe5-e845-4a81-a951-ceb7cd6d097c")]
internal sealed class HeightMapSdf : Instance<HeightMapSdf>
{
    [Output(Guid = "2a248aaf-452e-40b5-b90a-3defd7c89a2a")]
    public readonly Slot<ShaderGraphNode> Result = new Slot<ShaderGraphNode>();

    private enum CombineMethods
    {
        Union,
        UnionSoft,
        UnionRound,
    }

        [Input(Guid = "b870f95a-2858-4d9b-be78-8c74bcd5eab3")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> SdfImage = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "82a63a92-1d49-473f-92a9-169cba2cf735")]
        public readonly InputSlot<float> DisplacementHeight = new InputSlot<float>();

        [Input(Guid = "b9b08796-9997-41e8-8eb7-8d5ca02e93c6")]
        public readonly InputSlot<float> UvScale = new InputSlot<float>();

        [Input(Guid = "3045d688-05d6-4421-84be-a12bba5e30cb")]
        public readonly InputSlot<System.Numerics.Vector2> UvStretch = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "a56d9a84-5f09-43e3-81bc-34eb888b8e1a")]
        public readonly InputSlot<System.Numerics.Vector2> UvOffset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "c82a8a96-e7f8-4ee1-874e-44ed7449f76f")]
        public readonly InputSlot<float> MaxHeight = new InputSlot<float>();

        [Input(Guid = "f42f9439-38cc-4ff8-a3fc-51cc3d38f05e")]
        public readonly InputSlot<float> MaxSlope = new InputSlot<float>();
}