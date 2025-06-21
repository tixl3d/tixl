namespace Lib.field.generate.sdf;

/// <summary>
/// This operator only contains the shader setup to prepare a structuredBuffer with transform matrices
/// for each point (e.g. slightly improving the ray marching performance). See <see cref="ExecuteRepeatFieldAtPoints"/>
/// for the actual implementation of the IShaderGraph note.
/// </summary>
[Guid("2446df39-b455-428a-807d-73c0e6cbab79")]
internal sealed class Image2dSDF : Instance<Image2dSDF>
{
    [Output(Guid = "4a3cb304-17f2-4817-a8e1-369ea5361da8")]
    public readonly Slot<ShaderGraphNode> Result = new Slot<ShaderGraphNode>();

    private enum CombineMethods
    {
        Union,
        UnionSoft,
        UnionRound,
    }

        [Input(Guid = "c817ea37-e994-462b-ad8a-7e4cadf2f9a5")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> SdfImage = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "d7ed4ea1-0472-4535-8f39-fd440400d11d")]
        public readonly InputSlot<float> SdfScale = new InputSlot<float>();

        [Input(Guid = "1bcd6582-0e81-4980-bebe-ccf0d856299c")]
        public readonly InputSlot<System.Numerics.Vector2> ImageSize = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "59297115-f0cd-449e-be89-8a7ccbb0aa44")]
        public readonly InputSlot<float> Offset = new InputSlot<float>();
}