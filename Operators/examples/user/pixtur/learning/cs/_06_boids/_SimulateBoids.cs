namespace Examples.user.pixtur.learning.cs._06_boids;

[Guid("4f8c54a3-d146-4f7c-8628-17697b0201c9")]
public class _SimulateBoids : Instance<_SimulateBoids>
{
    [Output(Guid = "5f95c6f4-e654-4858-90fe-5dfa540e2186")]
    public readonly Slot<Texture2D> ImgOutput = new();

    [Input(Guid = "2d740120-1fe5-444f-80a0-2201c4a9bfcd")]
    public readonly InputSlot<int> ComputeSteps = new();

    [Input(Guid = "60edbf47-c594-4079-89cb-abffc2a5baa0")]
    public readonly InputSlot<int> AgentCount = new();

    [Input(Guid = "df993647-f384-4371-8196-2f52ba9e2d5a")]
    public readonly InputSlot<bool> RestoreLayoutEnabled = new();

    [Input(Guid = "cf868ea8-6a73-4c93-b435-b4e3a1a95167")]
    public readonly InputSlot<float> RestoreLayout = new();

    [Input(Guid = "01b6ac2b-be17-467c-bb64-69dc04194134")]
    public readonly InputSlot<float> EffectLayer = new();

    [Input(Guid = "fd92b9d4-cb43-4585-915a-9d4daac537dd")]
    public readonly InputSlot<float> EffectTwist = new();

    [Input(Guid = "e07deb43-5fc1-4600-83d1-b371cc52d12c")]
    public readonly InputSlot<Texture2D> EffectTexture = new();

    [Input(Guid = "d4881b3a-c91c-406d-b871-3d45b124cfd3")]
    public readonly InputSlot<float> Alpha = new();

    [Input(Guid = "ba33ad31-61ee-4276-8a72-33f5aa17d6fc")]
    public readonly InputSlot<T3.Core.DataTypes.StructuredList> BoidDefinitions = new();


}