namespace Lib.render.gizmo._{
    [Guid("3b97856c-5397-402e-85ca-5d227af348dd")]
    internal sealed class DrawSphere : Instance<DrawSphere>
    {
        [Output(Guid = "1221649e-bf7f-4f01-aee6-099f7a35469e")]
        public readonly Slot<Command> Output = new Slot<Command>();

        [Input(Guid = "c240d9cb-42ac-4ca3-92a6-86012b594e81")]
        public readonly InputSlot<System.Numerics.Vector3> Position = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "aa1307c2-c955-461b-8d43-b81c7e85a868")]
        public readonly InputSlot<float> Radius = new InputSlot<float>();

        [Input(Guid = "408bb779-8007-49c3-9015-b04419981b03")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();


    }
}

