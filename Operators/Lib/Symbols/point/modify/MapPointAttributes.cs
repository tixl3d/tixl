namespace Lib.point.modify;

[Guid("191e5057-4da4-447e-b7cf-e9e0ed8c5dd8")]
internal sealed class MapPointAttributes : Instance<MapPointAttributes>
{

    [Output(Guid = "39c62e1e-7c63-4a88-9923-3f7f5fffbfbf")]
    public readonly Slot<BufferWithViews> Output = new();

        [Input(Guid = "d504c3f9-290f-4a73-bf9d-f9266ea955f6")]
        public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

        [Input(Guid = "7634c477-6891-4fac-8f8a-3e580cb02277", MappedType = typeof(InputModes))]
        public readonly InputSlot<int> InputMode = new InputSlot<int>();

        [Input(Guid = "d5a5862b-d3a2-4e6e-ad54-32cff7ced0fd")]
        public readonly InputSlot<float> Strength = new InputSlot<float>();

        [Input(Guid = "3c94faca-61dd-4af6-883b-9eba1ed80b06", MappedType = typeof(StrengthFactors))]
        public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

        [Input(Guid = "840aa616-6983-4840-a58b-d5396a91c2f9", MappedType = typeof(MappingModes))]
        public readonly InputSlot<int> Mapping = new InputSlot<int>();

        [Input(Guid = "38046ede-e786-4cb1-ac17-de6cb7b91c32")]
        public readonly InputSlot<float> Range = new InputSlot<float>();

        [Input(Guid = "82a1a932-f5c3-41d5-9539-9b21663aee1b")]
        public readonly InputSlot<float> Phase = new InputSlot<float>();

        [Input(Guid = "7be4e933-6183-4e9c-9c10-0468b46f2a45", MappedType = typeof(WriteColorModes))]
        public readonly InputSlot<int> WriteColor = new InputSlot<int>();

        [Input(Guid = "7c944690-d5b2-4894-a178-97593ecd797a")]
        public readonly InputSlot<T3.Core.DataTypes.Gradient> Gradient = new InputSlot<T3.Core.DataTypes.Gradient>();

        [Input(Guid = "6d16847c-a560-4835-b120-3fb8c278530a", MappedType = typeof(WriteToModes))]
        public readonly InputSlot<int> WriteTo = new InputSlot<int>();

        [Input(Guid = "ba00fa7a-fcda-48da-a4c7-f2fe97997e50", MappedType = typeof(WriteModes))]
        public readonly InputSlot<int> WriteMode = new InputSlot<int>();

        [Input(Guid = "27bf737b-966e-4203-b8fd-2d9c7b19dcad")]
        public readonly InputSlot<T3.Core.DataTypes.Curve> MappingCurve = new InputSlot<T3.Core.DataTypes.Curve>();

        [Input(Guid = "cd91ff45-7f21-40fd-86c8-8dd95204c3b3")]
        public readonly InputSlot<T3.Core.DataTypes.Texture2D> ValueTexture = new InputSlot<T3.Core.DataTypes.Texture2D>();

    private enum InputModes
    {
        BufferOrder,
        F1,
        F2,
        Random,
    }

    private enum WriteToModes
    {
        None,
        F1,
        F2,
        Scale,
    }

    private enum MappingModes
    {
        Centered,
        ForStart,
        PingPong,
        Repeat,
        UseOriginalW,
    }
        
    private enum WriteModes
    {
        Replace,
        Multiply,
        Add,
    }
    
    private enum WriteColorModes
    {
        None,
        Replace,
        Multiply,
    }
    
    private enum StrengthFactors
    {
        None = 0,
        F1 = 1,
        F2 = 2,
    }
}