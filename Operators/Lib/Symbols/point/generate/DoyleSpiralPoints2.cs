namespace Lib.point.generate;

[Guid("eaeb8937-f6ff-4a4a-8f00-27484285cd2d")]
internal sealed class DoyleSpiralPoints2 : Instance<DoyleSpiralPoints2>
                                 ,ITransformable
{

    [Output(Guid = "197b9371-876f-4809-b030-bb1a7b622312")]
    public readonly TransformCallbackSlot<BufferWithViews> OutBuffer = new();

        
    public DoyleSpiralPoints2()
    {
        OutBuffer.TransformableOp = this;
    }

    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;

    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    [Input(Guid = "72ffae32-423d-4c4e-9b6a-39094b1d4bfd")]
    public readonly InputSlot<int> Steps = new InputSlot<int>();

    [Input(Guid = "ffbfa2dd-75ed-439e-8bab-eb0104ab9c0d")]
    public readonly InputSlot<float> Offset = new InputSlot<float>();

    [Input(Guid = "bfc2279a-7bf8-436a-a04d-813ecee1776a")]
    public readonly InputSlot<int> PointsPerStep = new InputSlot<int>();

    [Input(Guid = "77f01a41-38ff-4186-8336-84a030be124f")]
    public readonly InputSlot<int> SpiralSteepness = new InputSlot<int>();

    [Input(Guid = "c576de4b-d0da-4fb7-8290-e9fc383f7c13")]
    public readonly InputSlot<float> Scale = new InputSlot<float>();

    [Input(Guid = "5af0a50e-0da1-4800-9fef-c1217eb2368a")]
    public readonly InputSlot<float> ScaleBias = new InputSlot<float>();

    [Input(Guid = "51748370-6134-4297-90bd-cbd77531d854")]
    public readonly InputSlot<float> CenterPositionScale = new InputSlot<float>();

    [Input(Guid = "dcc7b167-c743-46d7-9a01-08c291a2016f")]
    public readonly InputSlot<float> W = new InputSlot<float>();

    [Input(Guid = "c0e1a16c-7b1f-43bb-aa13-42cd37f499b8")]
    public readonly InputSlot<float> WBias = new InputSlot<float>();

    [Input(Guid = "58e4f212-dc64-4697-a561-296bb66e39ca")]
    public readonly InputSlot<float> CenterSizeScale = new InputSlot<float>();

    [Input(Guid = "1e099abf-0378-43a4-899c-ade1828e42a6")]
    public readonly InputSlot<Vector3> Center = new InputSlot<Vector3>();

    [Input(Guid = "4f0a45cb-7d9b-4021-9977-471215c377a6")]
    public readonly InputSlot<Vector3> OrientationAxis = new InputSlot<Vector3>();

    [Input(Guid = "b8a087a7-2a72-45e8-9adf-938cca729651")]
    public readonly InputSlot<float> OrientationAngle = new InputSlot<float>();

    private enum SizeModes
    {
        Cell,
        Bounds,
    }
        
    private enum Tilings
    {
        Cartesian,
        Triangular,
        HoneyCombs
    }
}