namespace Lib.image.fx.stylize;

[Guid("01f405c6-097f-40c7-8b5a-3995565c0034")]
public class ColorPhysarum : Instance<ColorPhysarum>
{
    [Output(Guid = "3b586375-698a-4984-bb3c-1b5da6649e82")]
    public readonly Slot<Texture2D> ImgOutput = new();

        [Input(Guid = "02f1b76b-20a6-44f6-b9b6-107d241a597f")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> EffectTexture = new InputSlot<T3.Core.DataTypes.Texture2D>();

        [Input(Guid = "40b1eda0-506e-4cd7-803f-b22c3b5034c1")]
        public readonly InputSlot<float> RestoreLayout = new InputSlot<float>();

        [Input(Guid = "0403061c-e513-4f14-a53c-da6a9f974ab0")]
        public readonly InputSlot<bool> RestoreLayoutEnabled = new InputSlot<bool>();

        [Input(Guid = "def87ad5-85a3-4e3f-b41d-863bcdb9a274")]
        public readonly InputSlot<bool> ShowAgents = new InputSlot<bool>();

        [Input(Guid = "d7639210-98f5-4d88-8536-2969ffacc88f")]
        public readonly InputSlot<T3.Core.DataTypes.Vector.Int2> Resolution = new InputSlot<T3.Core.DataTypes.Vector.Int2>();

        [Input(Guid = "a9fc4458-8466-4c1e-9ff1-6a1027ac2452")]
        public readonly InputSlot<int> AgentCount = new InputSlot<int>();

        [Input(Guid = "3376f092-0f31-4f33-94aa-dc1d87765ca1")]
        public readonly InputSlot<int> ComputeSteps = new InputSlot<int>();

        [Input(Guid = "370abbce-6242-47ed-97e5-0449279bcdc3")]
        public readonly InputSlot<System.Numerics.Vector4> DecayRatio = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "f42d9fc4-0ab3-4fd4-a9c7-e352e4a1f2b9")]
        public readonly InputSlot<float> AngleLockSteps = new InputSlot<float>();

        [Input(Guid = "5f412281-381d-4f43-a3cd-a8145377f605")]
        public readonly InputSlot<float> AngleLockFactor = new InputSlot<float>();

        [Input(Guid = "9de1440a-12dd-41d3-9c64-b7f9673ceddf")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> CustomPoints = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "699612ce-8d27-4e18-a20b-0805ace4c2a3")]
        public readonly InputSlot<System.Numerics.Vector4> EmitStrength = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "a0639ac4-6873-4429-a517-edcf95eaa548")]
        public readonly InputSlot<System.Numerics.Vector4> ColorPickUp = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "fe1c94d9-83ab-4de7-a7c8-873a21cb7ec1")]
        public readonly InputSlot<System.Numerics.Vector4> ComfortZones = new InputSlot<System.Numerics.Vector4>();

        [Input(Guid = "c55aff93-3dbe-41d9-a95a-218fd97b73d0")]
        public readonly InputSlot<float> BaseMovement = new InputSlot<float>();

        [Input(Guid = "41ed47e8-3752-47ff-87cc-92dd321fad9a")]
        public readonly InputSlot<float> BaseRotation = new InputSlot<float>();

        [Input(Guid = "51ec134a-446c-4491-a800-ad031dc4173f")]
        public readonly InputSlot<float> SideAngle = new InputSlot<float>();

        [Input(Guid = "dce3fa87-ea7e-4efc-83c7-fd404d294384")]
        public readonly InputSlot<float> SideRadius = new InputSlot<float>();

        [Input(Guid = "8c4c5d7e-127c-421f-8ab3-5c434f804e4c")]
        public readonly InputSlot<float> FrontRadius = new InputSlot<float>();

        [Input(Guid = "f8fe6b09-b760-4bec-a17c-a2d4d2f5b07f")]
        public readonly InputSlot<float> MoveToComfort = new InputSlot<float>();

        [Input(Guid = "be543da0-fae2-4e1e-8f97-87ec37e4e2a0")]
        public readonly InputSlot<float> RotateToComfort = new InputSlot<float>();


}