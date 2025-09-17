namespace Examples.user.pixtur.learning.cs;

[Guid("108a0a0d-a8d9-4898-af04-0dea9eef8968")]
public class ComputeShaderCourse : Instance<ComputeShaderCourse>
{

    [Output(Guid = "4716a457-7d92-481a-b651-566bc453cfb9")]
    public readonly Slot<Texture2D> output = new();

}