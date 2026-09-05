namespace Examples.user.still.worksforeverybody;

[Guid("00485ce8-d342-4c97-aac3-1af8a7f03897")]
internal sealed class WorksForEverybody : Instance<WorksForEverybody>
{
    [Output(Guid = "de57341f-86c1-4426-b3c6-a5dc36490759")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}