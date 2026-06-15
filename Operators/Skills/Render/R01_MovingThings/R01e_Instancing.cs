namespace Skills.Render.R01_MovingThings;

[Guid("2fc0840b-4ba4-4919-aa2b-214e8ccc3d82")]
internal sealed class R01e_Instancing :Instance<R01e_Instancing>{
    [Output(Guid = "42bf4faf-8c00-4178-8416-d098e5137de0")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}