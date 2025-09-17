namespace Examples.user.pixtur.FastDemo2;

[Guid("20a10411-8a39-4ca3-85db-8f34537f66b8")]
public class FastDemo2 : Instance<FastDemo2>
{
    [Output(Guid = "7a5725b9-ba46-4e1b-a698-7ff78ae6cd1b")]
    public readonly Slot<Texture2D> ImgOutput = new();


}