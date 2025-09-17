namespace Examples.user.pixtur.learning.cs._06_boids;

[Guid("efad36a7-0be9-4b72-a5e8-b990a99bc864")]
public class _SimulateBoids3 : Instance<_SimulateBoids3>
{
    [Output(Guid = "44bb1234-4614-483b-9e8e-d0f6a96086c8")]
    public readonly Slot<Texture2D> ImgOutput = new();

    [Input(Guid = "25fd3ddd-818a-433e-9c0b-5799fdf3c146")]
    public readonly InputSlot<int> AgentCount = new();

    [Input(Guid = "df2b1b31-b8c2-4b50-9a44-6a04076659ee")]
    public readonly InputSlot<float> Test3 = new();

    [Input(Guid = "ac608812-4777-43bf-b9cc-d37effd8e0cf")]
    public readonly InputSlot<bool> RestoreLayoutEnabled = new();

    [Input(Guid = "c4fc0714-f832-4cfb-a660-f066b73825fc")]
    public readonly InputSlot<float> RestoreLayout = new();


}