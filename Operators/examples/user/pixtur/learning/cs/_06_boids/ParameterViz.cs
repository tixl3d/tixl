namespace Examples.user.pixtur.learning.cs._06_boids;

[Guid("c6cf23b5-3085-4488-9e9e-5e09aa88a9fa")]
public class ParameterViz : Instance<ParameterViz>
{
    [Output(Guid = "dcc59129-7db3-454d-8741-7d2ff3fa4cf8")]
    public readonly Slot<Command> Output = new();

    [Input(Guid = "d77c2bb3-6964-4ac7-94be-793e93f5a710")]
    public readonly InputSlot<string> Name = new();

    [Input(Guid = "932c87f3-1839-40d3-83c3-545153988ea8")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "fd3921d7-9137-409a-b29a-35ff74f2ae2b")]
    public readonly InputSlot<float> Ratio = new();

    [Input(Guid = "a2568738-aa73-4a07-af40-99c33710065c")]
    public readonly InputSlot<System.Numerics.Vector3> Position = new();

    [Input(Guid = "32aff693-7241-4f8b-ac94-e58f2b3081bb")]
    public readonly InputSlot<System.Numerics.Vector4> Color = new();
}