namespace Lib.field.use;

[Guid("67f77fd3-7fe3-49ca-bc5b-34b92aa7ab00")]
internal sealed class ApplyVectorField : Instance<ApplyVectorField>
{
    [Output(Guid = "0cee6111-4d84-4436-894a-472aa61571cc")]
    public readonly Slot<BufferWithViews> Result2 = new();

    [Input(Guid = "3f49f5f7-4856-41aa-8196-7495fb636aea")]
    public readonly InputSlot<T3.Core.DataTypes.BufferWithViews> Points = new InputSlot<T3.Core.DataTypes.BufferWithViews>();

    [Input(Guid = "a6ab6b01-9cd5-45f0-98d4-476fee3c9500")]
    public readonly InputSlot<float> Strength = new InputSlot<float>();

    [Input(Guid = "b4d7ac0b-92a4-44ed-8e64-717c560176bd", MappedType = typeof(FModes))]
    public readonly InputSlot<int> StrengthFactor = new InputSlot<int>();

    [Input(Guid = "3a6cde33-48e9-4433-a654-e530f7cd8a2a")]
    public readonly InputSlot<bool> Normalize = new InputSlot<bool>();

    [Input(Guid = "8346d059-b9df-4a61-a9da-31bc4ff37543")]
    public readonly InputSlot<ShaderGraphNode> VectorField = new InputSlot<ShaderGraphNode>();

        [Input(Guid = "6d1eeced-11f1-420b-9a2c-3f784d3d4835")]
        public readonly InputSlot<System.Numerics.Vector3> UpVector = new InputSlot<System.Numerics.Vector3>();

        [Input(Guid = "9aae8917-40ad-4ce5-aac4-da2de48cdb62")]
        public readonly InputSlot<float> ClampLength = new InputSlot<float>();

        [Input(Guid = "ccc4cb54-af87-4c3d-9759-c4409b092281")]
        public readonly InputSlot<float> ScaleLength = new InputSlot<float>();

        [Input(Guid = "9304f0cd-e210-4478-9235-7bb6210f3534", MappedType = typeof(SetFxModes))]
        public readonly InputSlot<int> SetFx1To = new InputSlot<int>();

        [Input(Guid = "739a735c-677b-4b56-892c-046eb7af239f", MappedType = typeof(SetFxModes))]
        public readonly InputSlot<int> SetFx2To = new InputSlot<int>();
        
    private enum FModes
    {
        None,
        F1,
        F2,
    }
    
    private enum SetFxModes
    {
        Keep,
        Magnitude,
        Distance,
    }
}