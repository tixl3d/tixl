namespace Lib.image.fx.feedback;

[Guid("33424f7f-ea2d-4753-bbc3-8df00830c4b5")]
internal sealed class AdvancedFeedback : Instance<AdvancedFeedback>
{
    [Output(Guid = "a977681b-2f7b-44de-a29e-3ba00e2260b0")]
    public readonly Slot<Texture2D> ColorBuffer = new();

        [Input(Guid = "0f29ca23-b5fb-484e-8ae6-8ed70d67d623")]
        public readonly MultiInputSlot<T3.Core.DataTypes.Command> Command = new MultiInputSlot<T3.Core.DataTypes.Command>();

        [Input(Guid = "1d5207f0-132e-42e9-9b2b-171d092d6cac")]
        public readonly InputSlot<float> Displacement = new InputSlot<float>();

        [Input(Guid = "95b99630-6d27-41d6-9e02-a3e905d023d7")]
        public readonly InputSlot<float> Shade = new InputSlot<float>();

        [Input(Guid = "f1a53a46-fa5c-49af-9d53-0d68cfe1b33e")]
        public readonly InputSlot<float> BlurRadius = new InputSlot<float>();

        [Input(Guid = "882acab3-7cb9-42e8-8f63-5382c83422c2")]
        public readonly InputSlot<float> SampleDistance = new InputSlot<float>();

        [Input(Guid = "37f312f0-949c-4a43-af92-786761b03d3b")]
        public readonly InputSlot<float> SampleRadius = new InputSlot<float>();

        [Input(Guid = "fc1b2bc8-6756-4e78-a90b-8af691d85875")]
        public readonly InputSlot<float> Twist = new InputSlot<float>();

        [Input(Guid = "d2b9ef03-1641-4949-86ff-b71dc1fb3ad0")]
        public readonly InputSlot<float> Zoom = new InputSlot<float>();

        [Input(Guid = "849540c3-7ffd-40a7-a78b-3d051256e5f1")]
        public readonly InputSlot<float> Rotate = new InputSlot<float>();

        [Input(Guid = "482fba6b-f92e-418c-a8f4-8da0f546c4a6")]
        public readonly InputSlot<System.Numerics.Vector2> Offset = new InputSlot<System.Numerics.Vector2>();

        [Input(Guid = "3ae3bc6a-5950-4f8b-b2c1-5247dfb9221c")]
        public readonly InputSlot<float> DisplaceOffset = new InputSlot<float>();

        [Input(Guid = "b454ebb9-6b27-46b1-962e-15e658c8f6a2")]
        public readonly InputSlot<float> ShiftHue = new InputSlot<float>();

        [Input(Guid = "aedb3315-5061-4b68-8925-761d4d8c78b4")]
        public readonly InputSlot<float> ShiftSaturation = new InputSlot<float>();

        [Input(Guid = "cbe45989-b239-491e-a2d8-83a42ab58b85")]
        public readonly InputSlot<float> ShiftBrightness = new InputSlot<float>();

        [Input(Guid = "9d210666-6b60-4193-9bfd-8a50c8973238")]
        public readonly InputSlot<float> LimitBrights = new InputSlot<float>();

        [Input(Guid = "640de6a7-14ce-4c72-b78e-aa72f642b765")]
        public readonly InputSlot<float> AmplifyEdges = new InputSlot<float>();

        [Input(Guid = "af523ebc-0791-430c-8fff-e50779b8af4f")]
        public readonly InputSlot<bool> Reset = new InputSlot<bool>();

        [Input(Guid = "c3827e2e-2d15-475c-9b2f-03a861e97fc5")]
        public readonly InputSlot<bool> IsEnabled = new InputSlot<bool>();

}