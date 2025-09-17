namespace Examples.user.pixtur.project.sceneCityLogo;

[Guid("b849e7a2-2fee-4463-b964-67f5c2519fc5")]
public class SceneCityLogo : Instance<SceneCityLogo>
{
    [Output(Guid = "0691231b-64cc-40a4-aa54-d31285d0928b")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}