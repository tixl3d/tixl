namespace Examples.Lib.mesh.generate;

[Guid("76cd7578-0f97-49a6-938a-caeaa98deaac")]
internal sealed class ChunkInstancingExample : Instance<ChunkInstancingExample>
{
    [Output(Guid = "95dee492-83bc-4b09-97bf-a8c7d20123ac")]
    public readonly Slot<Command> Update = new ();
}