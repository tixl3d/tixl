namespace Lib.physics;

/// <summary>
/// Describes a force for <see cref="PhysicsWorld"/>. Like <see cref="RigidBody"/> this is pure data: the
/// world applies it to its dynamic bodies while it steps. A continuous force accelerates the bodies every
/// step; an impulse changes their velocity once, each time <see cref="Trigger"/> turns on.
/// </summary>
[Guid("001a1112-e722-4bdb-b6f2-bb2456643c02")]
internal sealed class RigidForce : Instance<RigidForce>
{
    [Output(Guid = "7e787098-3f49-49c4-8718-7ad0b6793026", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<PhysicsForce> OutForce = new();

    public RigidForce()
    {
        _descriptor.Id = _id;
        OutForce.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _descriptor.Type = (PhysicsForceType)ForceType.GetValue(context);
        _descriptor.Position = Position.GetValue(context);
        _descriptor.Direction = Direction.GetValue(context);
        _descriptor.Strength = Strength.GetValue(context);
        _descriptor.Radius = Radius.GetValue(context);
        _descriptor.Falloff = Falloff.GetValue(context);
        _descriptor.ScaleByMass = ScaleByMass.GetValue(context);
        _descriptor.IsImpulse = IsImpulse.GetValue(context);
        _descriptor.Field = Field.GetValue(context);
        _descriptor.CollisionGroup = CollisionGroup.GetValue(context);
        _descriptor.Enabled = Enabled.GetValue(context);

        // The world fires an impulse when this count changes, so a trigger held for many frames fires once.
        var isTriggered = Trigger.GetValue(context);
        if (isTriggered && !_wasTriggered)
            _descriptor.TriggerCount++;

        _wasTriggered = isTriggered;

        OutForce.Value = _descriptor;
        OutForce.DirtyFlag.Invalidate();
    }

    private readonly Guid _id = Guid.NewGuid();
    private readonly PhysicsForce _descriptor = new();
    private bool _wasTriggered;

    [Input(Guid = "65b63902-756a-42a8-b027-2d5a26332f08", MappedType = typeof(PhysicsForceType))]
    public readonly InputSlot<int> ForceType = new(0);

    [Input(Guid = "12531efe-7296-40c4-a894-4eb2bf46de14")]
    public readonly InputSlot<Vector3> Position = new();

    [Input(Guid = "edd7245d-d227-4f4c-a840-a68a14d7e71d")]
    public readonly InputSlot<Vector3> Direction = new(Vector3.UnitY);

    [Input(Guid = "4b020f6c-2875-42f1-b1d7-3fe2f53a57b6")]
    public readonly InputSlot<float> Strength = new(1f);

    [Input(Guid = "5f0608b7-ce9f-4b7c-83f3-fcff5e45bb7f")]
    public readonly InputSlot<float> Radius = new();

    [Input(Guid = "aa70a793-a6f2-4363-9a6a-341ef160336e")]
    public readonly InputSlot<float> Falloff = new(1f);

    [Input(Guid = "34707a7a-88fc-45c2-84dc-9e4efef74a7d")]
    public readonly InputSlot<VectorField> Field = new();

    [Input(Guid = "9b65922c-50c8-4a66-8b14-f01d82006d73")]
    public readonly InputSlot<bool> ScaleByMass = new();

    [Input(Guid = "bd4ab7bc-644d-4958-b6eb-407f5d30a42a")]
    public readonly InputSlot<bool> IsImpulse = new();

    [Input(Guid = "9c51f502-2305-4927-8502-6051d51846ff")]
    public readonly InputSlot<bool> Trigger = new();

    [Input(Guid = "35656afd-b2bd-4006-a7a3-552c2c9d47cb")]
    public readonly InputSlot<int> CollisionGroup = new(0);

    [Input(Guid = "7eea3f9b-24f0-4c1a-8059-dcaf4655478f")]
    public readonly InputSlot<bool> Enabled = new(true);
}
