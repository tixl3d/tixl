namespace Types.Routing;

// Passes T3.Core.DataTypes.SceneSetup through the .t3 connection; no C# evaluator is needed.
[Guid("d10bb007-dc97-4b8d-baa6-bbe290d90fe7")]
public sealed class RerouteSceneSetup : Instance<RerouteSceneSetup>, IRerouteNode
{
    [Output(Guid = "4bcbc5c8-f231-4535-a270-60c154dcc2d8")]
    public readonly Slot<T3.Core.DataTypes.SceneSetup> Output = new();

    [Input(Guid = "fef80d4b-3602-4dac-8ded-bcb5d81b6228")]
    public readonly InputSlot<T3.Core.DataTypes.SceneSetup> Input = new();
}
