using T3.Core.Utils;

namespace Lib.particle.force;

[Guid("87f68a6d-e13d-49b7-ab0c-c24c0cefd453")]
internal sealed class SwitchParticleForce : Instance<SwitchParticleForce>
{
    [Output(Guid = "84B9A857-89B3-4B30-A5E7-152063088773")]
    public readonly Slot<T3.Core.DataTypes.ParticleSystem> Selected = new();

    public SwitchParticleForce()
    {
        Selected.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var index = Index.GetValue(context);
        if (index == -1)
            return;
        
        var connections = Input.GetCollectedTypedInputs();
        if (connections == null || connections.Count == 0)
            return;
        
        Selected.Value = connections[index.Mod(connections.Count)].GetValue(context);
    }

    [Input(Guid = "0E7BA3EA-D20A-41D6-9FD9-00A81D39F112")]
    public readonly MultiInputSlot<T3.Core.DataTypes.ParticleSystem> Input = new();

    [Input(Guid = "1babcb7f-7d74-4133-83b9-f3e47777beeb")]
    public readonly InputSlot<int> Index = new(0);
}