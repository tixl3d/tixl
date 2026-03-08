namespace Lib.render.gizmo._{
    [Guid("3b97856c-5397-402e-85ca-5d227af348dd")]
    internal sealed class DrawSphere : Instance<DrawSphere>, ITransformable
    {
        [Output(Guid = "1221649e-bf7f-4f01-aee6-099f7a35469e")]
        public readonly TransformCallbackSlot<Command> Output = new();
        
        public DrawSphere()
        {
            Output.TransformableOp = this;
        }        
        
        IInputSlot ITransformable.TranslationInput => Position;
        IInputSlot ITransformable.RotationInput => null;
        IInputSlot ITransformable.ScaleInput => null;
        
        public Action<Instance, EvaluationContext> TransformCallback { get; set; }
        
        
        [Input(Guid = "c240d9cb-42ac-4ca3-92a6-86012b594e81")]
        public readonly InputSlot<System.Numerics.Vector3> Position = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "aa1307c2-c955-461b-8d43-b81c7e85a868")]
        public readonly InputSlot<float> Radius = new InputSlot<float>();

        [Input(Guid = "408bb779-8007-49c3-9015-b04419981b03")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "347f8abe-dbd9-4939-9e28-4f25c73dddc9")]
        public readonly InputSlot<bool> WireFrame = new InputSlot<bool>();

        [Input(Guid = "7a1f7dc7-e716-4a07-bc46-bfc8d717a083")]
        public readonly InputSlot<SharpDX.Direct3D11.CullMode> CullMode = new InputSlot<SharpDX.Direct3D11.CullMode>();

        
        

    }
}

