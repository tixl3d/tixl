using T3.Core.Utils;

namespace Lib.render.shading;

[Guid("54ba8673-ff58-48d1-ae2e-ee2b83bc6860")]
internal sealed class IntToWrapmode : Instance<IntToWrapmode>
{
    [Output(Guid = "D3E48911-F6A6-439F-B34A-84FE9D75B388")]
    public readonly Slot<TextureAddressMode> Selected = new();

    public IntToWrapmode()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var index = ModeIndex.GetValue(context)
                             .Clamp((int)TextureAddressMode.Wrap,
                                    (int)TextureAddressMode.MirrorOnce);
        Selected.Value = CastTo<TextureAddressMode>.From(index);
    }

    [Input(Guid = "F50C736B-DC80-424B-8517-AF0CA4168666", MappedType = typeof(TextureAddressMode))]
    public readonly InputSlot<int> ModeIndex = new(0);
}