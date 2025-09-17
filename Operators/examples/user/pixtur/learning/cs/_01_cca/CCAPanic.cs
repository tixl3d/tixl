namespace Examples.user.pixtur.learning.cs._01_cca;

[Guid("4cbd1314-6da8-40b7-b24f-04725b98d3f4")]
public class CCAPanic : Instance<CCAPanic>
{
    [Output(Guid = "a033a4bb-1d3b-4795-a9d9-0e515f6e2f38")]
    public readonly Slot<Texture2D> Output = new();


}