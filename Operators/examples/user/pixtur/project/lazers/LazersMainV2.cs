namespace Examples.user.pixtur.project.lazers;

[Guid("275b0dfd-be60-40f8-9e0f-5d1ebe0fe4b4")]
public class LazersMainV2 : Instance<LazersMainV2>
{
    [Output(Guid = "df9f6e17-cc14-45ef-8fee-92ad8df7abaa")]
    public readonly Slot<Texture2D> ImgOutput = new();


}