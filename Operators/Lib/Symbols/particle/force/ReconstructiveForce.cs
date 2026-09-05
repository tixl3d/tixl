namespace Lib.particle.force;

[Guid("eeec02e2-7db9-4132-935a-4caf03c828c6")]
internal sealed class ReconstructiveForce : Instance<ReconstructiveForce>
                                  ,ITransformable
{
    [Output(Guid = "4b7931a8-aace-41e8-ba38-db68d03d5cc2")]
    public readonly TransformCallbackSlot<T3.Core.DataTypes.ParticleSystem> ParticleSystem = new();

    // public SurfaceForce()
    // {
    //     ParticleSystem.TransformableOp = this;
    // }
    public ReconstructiveForce()
    {
        ParticleSystem.TransformableOp = this;
    }
        
    IInputSlot ITransformable.TranslationInput => VolumeCenter;
    IInputSlot ITransformable.RotationInput => VolumeRotate;
    IInputSlot ITransformable.ScaleInput => VolumeScale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

        [Input(Guid = "2a8f498f-b91e-41ad-a7d2-8082d5e72f39")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> TargetPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "5c3e885d-ba4c-4a8d-aaa0-f0d942e1457a")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "f6cb95fe-7079-4064-bac3-e36bc648da10", MappedType = typeof(DistanceModes))]
        public readonly InputSlot<int> DistanceMode = new InputSlot<int>();

        [Input(Guid = "6be3ff09-439f-4506-86d9-27e70422bb41")]
        public readonly InputSlot<System.Numerics.Vector3> VolumeCenter = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "4b253a7c-0e50-477d-8eee-4f4c9a5608e9")]
        public readonly InputSlot<System.Numerics.Vector3> VolumeStretch = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "def19ffe-41d3-44a3-9b32-2ae9bb2f2fe4")]
        public readonly InputSlot<float> VolumeScale = new InputSlot<float>();

        [Input(Guid = "9b3a6a71-829e-47cd-87f2-fe87d7848120")]
        public readonly InputSlot<System.Numerics.Vector3> VolumeRotate = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "77a64b13-445c-4d77-82ca-204f67c2f2cf")]
        public readonly InputSlot<float> FallOff = new InputSlot<float>();

        [Input(Guid = "aa4f0f7c-0ccc-45e8-aaa4-a8a2cf0657ed")]
        public readonly InputSlot<float> Bias = new InputSlot<float>();

        [Input(Guid = "b5f61cd0-ad6f-45fe-ad0f-8a7a716b7c8f")]
        public readonly InputSlot<T3.Core.Operator.GizmoVisibility> GizmoVisibility = new InputSlot<T3.Core.Operator.GizmoVisibility>();

    private enum DistanceModes
    {
        UsePointsPosition,
        UseTargetsPosition,
    }
}