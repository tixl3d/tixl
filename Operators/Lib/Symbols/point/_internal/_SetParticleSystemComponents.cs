namespace Lib.point._internal;

[Guid("705df4fe-8f91-4b1e-a7d1-432011ffcb3f")]
internal sealed class _SetParticleSystemComponents : Instance<_SetParticleSystemComponents>
{
    [Output(Guid = "9d729d46-06e2-4152-a5d7-3368ae5d737a", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Command> Output = new();

    public _SetParticleSystemComponents()
    {
        Output.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        //_particleSystem.PointBuffer = PointsBuffer.GetValue(context);
        _particleSystem.ParticleBuffer = PointsSimBuffer.GetValue(context);
        _particleSystem.SpeedFactor = SpeedFactor.GetValue(context);
        _particleSystem.InitializeVelocityFactor = InitializeVelocityFactor.GetValue(context);
        _particleSystem.IsReset = IsReset.GetValue(context);
        
        var effects = Forces.CollectedInputs;
        var keep = context.ParticleSystem;
        if (effects != null)
        {
            context.ParticleSystem = _particleSystem;
                
            // execute commands
            for (int i = 0; i < effects.Count; i++)
            {
                // This would be great to reuse forces from multiple Particle systems
                // but it's not working out of the box.
                //DirtyFlag.InvalidationRefFrame++;
                //effects[i].Invalidate();                
                effects[i].GetValue(context);
            }
        }

        context.ParticleSystem = keep;
            
        Forces.DirtyFlag.Clear();
    }

    private readonly ParticleSystem _particleSystem = new();
    
    [Input(Guid = "13583F72-3F77-4BE0-B596-B8DBD27CA19C")]
    public readonly InputSlot<BufferWithViews> PointsSimBuffer = new();
        
    [Input(Guid = "083C9379-FC0A-4D35-B056-1A639F739321")]
    public readonly InputSlot<float> SpeedFactor = new();
        
    [Input(Guid = "F3AB1099-3A0D-409E-AA18-89219E85E01F")]
    public readonly InputSlot<float> InitializeVelocityFactor = new();
        
    [Input(Guid = "73128257-D731-4065-B19A-C8FA21803CD4")]
    public readonly MultiInputSlot<ParticleSystem> Forces = new();
    
    [Input(Guid = "D3B6EA62-7D52-4791-B44F-3BA8EFAC93DE")]
    public readonly InputSlot<bool> IsReset = new();

    
}