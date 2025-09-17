namespace Examples.user.pixtur.research;

[Guid("dbe329c8-f16a-4ebb-bfab-2f90ed9646a2")]
public class ColorFlowExperiment1 : Instance<ColorFlowExperiment1>
{
    [Output(Guid = "d5d36e56-bbf1-4992-a983-240454e87768")]
    public readonly Slot<Texture2D> Output = new();


}