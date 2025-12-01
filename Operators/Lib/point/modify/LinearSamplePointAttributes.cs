namespace Lib.point.modify;

[Guid("bb4803d2-0c23-470a-94a8-c477e4f7dd8c")]
internal sealed class LinearSamplePointAttributes : Instance<LinearSamplePointAttributes>
{
    [Output(Guid = "47c23c59-fee8-4c77-a479-8f5684a3cd5c")]
    public readonly Slot<BufferWithViews> OutBuffer = new ();
        
    [Input(Guid = "36ca4c5d-5178-4b0c-9fdd-8752e819ff5b")]
    public readonly InputSlot<BufferWithViews> GPoints = new();

    [Input(Guid = "5363156c-b07c-4726-adb6-c14200713366", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Brightness = new();

    [Input(Guid = "9eef587c-9fce-480e-8a2f-ade488411616")]
    public readonly InputSlot<float> BrightnessFactor = new();

    [Input(Guid = "ee96fcc4-974c-47f3-8e07-01d3c29992d0")]
    public readonly InputSlot<float> BrightnessOffset = new();

    [Input(Guid = "e88125ba-0f5f-4853-848b-829575f684e9", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Red = new();

    [Input(Guid = "159bf880-b6b7-413b-afc9-acdc1b87a9e4")]
    public readonly InputSlot<float> RedFactor = new();

    [Input(Guid = "4b95d0f7-6806-48aa-b85e-b25b9ea2c926")]
    public readonly InputSlot<float> RedOffset = new();

    [Input(Guid = "2914dafc-1222-4067-b3bd-e80c4f7df6e4", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Green = new();

    [Input(Guid = "38484dbf-76b7-4eb9-a72e-2dd6a0d657fa")]
    public readonly InputSlot<float> GreenFactor = new();

    [Input(Guid = "20b1a7dd-d35d-4404-ac3d-0deca21296fc")]
    public readonly InputSlot<float> GreenOffset = new();

    [Input(Guid = "ebddd155-ce56-4c40-8257-8b58f529c20b", MappedType = typeof(Attributes))]
    public readonly InputSlot<int> Blue = new();

    [Input(Guid = "3deba352-f501-4d62-969d-826e5adbfa59")]
    public readonly InputSlot<float> BlueFactor = new();

    [Input(Guid = "81ba4ba1-6372-4703-9fa1-1998f0871d28")]
    public readonly InputSlot<float> BlueOffset = new();
        
        
    [Input(Guid = "a1c9f878-9b72-43e5-8df6-e1f592b4bf57", MappedType = typeof(Modes))]
    public readonly InputSlot<int> Mode = new();

    [Input(Guid = "eee2abdd-56ec-47f2-8510-03f73722c3af", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> TranslationSpace = new();

    [Input(Guid = "01d6866f-b212-4c65-b761-33803f4f790e", MappedType = typeof(Spaces))]
    public readonly InputSlot<int> RotationSpace = new();

        
    [Input(Guid = "743616e2-817a-4b39-a3b1-58b58f3465b2")]
    public readonly InputSlot<Texture2D> Texture = new();

        [Input(Guid = "b14c547a-4dfb-487f-bb03-cc1aa0219175")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "0a5c4f07-53d5-4329-8cb4-4b615e243df9", MappedType = typeof(FModes))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();


    private enum Attributes
    {
        NotUsed =0,
        For_X = 1,
        For_Y =2,
        For_Z =3,
        For_F1 =4,
        Rotate_X =5,
        Rotate_Y =6 ,
        Rotate_Z =7,
        Stretch_X = 8,
        Stretch_Y = 9,
        Stretch_Z = 10,
        For_F2 =11,
    }

    private enum Modes
    {
        Add,
        Multiply,
    }
        
    private enum Spaces
    {
        Object,
        Point,
    }
    
    private enum FModes
    {
        None,
        F1,
        F2,
    }
}