using T3.Core.Utils;

namespace Lib.point.draw;

[Guid("37a747b0-ec0e-4ebc-83dd-2e03022ad100")]
internal sealed class DrawRibbons : Instance<DrawRibbons>
{
    [Output(Guid = "324f8114-dae9-4cc8-aa88-021d84dbaf79", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "bbec658b-84fa-4528-ad03-6b306531b152")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "22a23dbc-0222-441d-8435-b630dcd77acb")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "d41169ee-4e53-4198-b56a-b4b771cc3dfd")]
    public readonly InputSlot<float> Width = new();

    [Input(Guid = "3f8b336d-fb2b-4b8a-b13a-a229e7792f46")]
    public readonly InputSlot<float> Spin = new();

    [Input(Guid = "cdaf942a-a518-4dd0-aea7-737aa11436bb")]
    public readonly InputSlot<float> Twist = new();

    [Input(Guid = "1e3af280-2f64-423d-b14d-630065659afc", MappedType = typeof(TextureModes))]
    public readonly InputSlot<int> TextureMode = new();

    [Input(Guid = "3198a61e-94b3-42c4-a2ae-822456db8bdd")]
    public readonly InputSlot<Vector2> TextureRange = new();

    [Input(Guid = "1ce27f43-3664-44e6-9a0b-5fcca3a5b9fe")]
    public readonly InputSlot<bool> EnableDepthWrite = new();

    [Input(Guid = "5124b85d-5c09-4329-bf33-ef3cc13f30aa", MappedType = typeof(SharedEnums.BlendModes))]
    public readonly InputSlot<int> BlendMod = new();

    [Input(Guid = "99252905-B0F0-48BB-AA92-39FFB5CD949C")]
    public readonly InputSlot<CullMode> Culling = new();

    [Input(Guid = "31791971-8c6e-4f8f-8b04-a3abf02ad69b")]
    public readonly InputSlot<bool> UseWAsWeight = new();
        
    private enum TextureModes
    {
        RelativeStartEnd,
        StartRepeat,
        Tile,
        UseW,
    }
}