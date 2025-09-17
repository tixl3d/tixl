namespace Examples.user.pixtur.dailies;

[Guid("58beef77-d3ed-4239-8e48-44aa2546dd39")]
public class Dailies2020_06_16 : Instance<Dailies2020_06_16>
{
    [Output(Guid = "5401bfcd-747f-41f6-bc10-e650198b18bf")]
    public readonly Slot<Texture2D> TextureOutput = new();

    [Output(Guid = "1c531799-0580-4814-818c-21ce444532ae")]
    public readonly Slot<Texture2D> Output2 = new();


}