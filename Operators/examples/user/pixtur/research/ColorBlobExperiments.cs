namespace Examples.user.pixtur.research;

[Guid("1977060f-1f72-4829-96aa-3b96c81bbae0")]
public class ColorBlobExperiments : Instance<ColorBlobExperiments>
{
    [Output(Guid = "40e80805-2643-46ec-a283-d5b64e002823")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}