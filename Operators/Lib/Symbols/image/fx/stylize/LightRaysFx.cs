namespace Lib.image.fx.stylize;

[Guid("53ff1a68-e888-444f-9ccc-23239c94d6db")]
internal sealed class LightRaysFx : Instance<LightRaysFx>
{
    [Output(Guid = "bdc413f2-9a15-4333-baba-aa57f73dda1a")]
    public readonly Slot<Texture2D> Output = new();

    [Input(Guid = "9d4f4e29-b2fe-415b-90f1-18390b520346")]
    public readonly InputSlot<Texture2D> Image = new InputSlot<Texture2D>();

    [Input(Guid = "e110c111-34d5-4251-87eb-6cb56d2a026e")]
    public readonly InputSlot<Texture2D> TextureFX = new InputSlot<Texture2D>();

    [Input(Guid = "7d60c1e1-4924-48e8-991f-df83370c9a30")]
    public readonly InputSlot<Vector2> Direction = new InputSlot<Vector2>();

    [Input(Guid = "c661ae7d-1cac-482f-b200-2d47946c22db")]
    public readonly InputSlot<int> Samples = new InputSlot<int>();

    [Input(Guid = "a1c47663-95a2-4a26-9b9a-f7e109868a21")]
    public readonly InputSlot<float> Length = new InputSlot<float>();

    [Input(Guid = "af5bfaf5-4d4f-4a51-9316-7dfd508e4fdb")]
    public readonly InputSlot<Vector4> RayColor = new InputSlot<Vector4>();

    [Input(Guid = "b26c20c8-ec60-4215-a53a-3e21cd98ea03")]
    public readonly InputSlot<float> Decay = new InputSlot<float>();

    [Input(Guid = "16e0b835-ad13-4b25-af88-48fef5f1d6a2")]
    public readonly InputSlot<float> ApplyFXToBackground = new InputSlot<float>();

    [Input(Guid = "9577dbad-f5a0-4602-b5e6-7a9772cd5290")]
    public readonly InputSlot<float> Amount = new InputSlot<float>();

}