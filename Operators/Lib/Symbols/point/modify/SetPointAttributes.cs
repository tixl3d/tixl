namespace Lib.point.modify;

[Guid("86b61bcf-4eaa-4f77-a535-8a1dc876aada")]
internal sealed class SetPointAttributes : Instance<SetPointAttributes>
{

    [Output(Guid = "9bc53d1e-64bf-4373-9367-66ffa41447bd")]
    public readonly Slot<BufferWithViews> Output = new();

        [Input(Guid = "29f16973-2bd7-4655-b32f-1a5b932010a1")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "cc54c0ab-28c1-4333-a016-1147b5aa44fb")]
        public readonly InputSlot<float> Amount = new InputSlot<float>();

        [Input(Guid = "66d0755b-d816-474a-9a95-3d11bcce59ee", MappedType = typeof(StrengthFactors))]
        public readonly InputSlot<int> AmountFactor = new InputSlot<int>();

        [Input(Guid = "72327b8d-2a32-4225-b6f9-294170dca7bf")]
        public readonly InputSlot<bool> SetPosition = new InputSlot<bool>();

        [Input(Guid = "8c2da7f6-4dd1-4691-b1a2-0640d4676750")]
        public readonly InputSlot<System.Numerics.Vector3> Position = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "1709c405-238e-4de8-9e3f-714f2740c7f6")]
        public readonly InputSlot<bool> SetRotation = new InputSlot<bool>();

        [Input(Guid = "cd16d07c-8487-4fdf-bf06-b5b8c8fea3c1")]
        public readonly InputSlot<System.Numerics.Vector3> RotationAxis = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "79177005-4d7b-462b-9308-a22865598266")]
        public readonly InputSlot<float> RotationAngle = new InputSlot<float>();

        [Input(Guid = "4d67dbfb-8f8c-4c49-9ee0-733bdea9a9e4")]
        public readonly InputSlot<bool> SetExtend = new InputSlot<bool>();

        [Input(Guid = "ef3adb10-b987-4dc9-b2eb-55d80a71a305")]
        public readonly InputSlot<System.Numerics.Vector3> Extend = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "35b5e37b-8d84-40ca-9995-c7ccde13d76e")]
        public readonly InputSlot<bool> SetFx1 = new InputSlot<bool>();

        [Input(Guid = "421bdf54-5b50-4c28-8370-576c1f3b265c")]
        public readonly InputSlot<float> Fx1 = new InputSlot<float>();

        [Input(Guid = "282478b1-4951-4302-a7fa-4b1cafd93018")]
        public readonly InputSlot<bool> SetFx2 = new InputSlot<bool>();

        [Input(Guid = "2ae84377-e382-4614-b2db-b3b31b601b91")]
        public readonly InputSlot<float> Fx2 = new InputSlot<float>();

        [Input(Guid = "b8ab4e0e-2c75-42af-9aec-865785f68091")]
        public readonly InputSlot<bool> SetColor = new InputSlot<bool>();

        [Input(Guid = "b4c4414d-e24d-4456-8c7d-00eb9de89de9")]
        public readonly InputSlot<System.Numerics.Vector4> Color = new InputSlot<System.Numerics.Vector4>();


    private enum MappingModes
    {
        Normal,
        ForStart,
        PingPong,
        Repeat,
        UseOriginalW,
    }
        
    private enum Modes
    {
        Replace,
        Multiply,
        Add,
    }
    
    private enum StrengthFactors
    {
        None = 0,
        F1 = 1,
        F2 = 2,
    }
}