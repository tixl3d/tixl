namespace Examples.user.pixtur.project.barnvj;

[Guid("ae52baa3-9bd8-4e35-95c7-4811a55eaf7d")]
public class BarnVJExperiment : Instance<BarnVJExperiment>
{
    [Output(Guid = "fa5efe86-4faa-463a-a4fd-ba83ec41ddd1")]
    public readonly Slot<Texture2D> ColorBuffer = new();


}