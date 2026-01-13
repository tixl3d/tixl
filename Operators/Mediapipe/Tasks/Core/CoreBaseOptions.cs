using Google.Protobuf;
using Mediapipe.Tasks.Core.Proto;

namespace Mediapipe.Tasks.Core;

public sealed class CoreBaseOptions(
    CoreBaseOptions.Delegate delegateCase = CoreBaseOptions.Delegate.CPU,
    string? modelAssetPath = null,
    byte[]? modelAssetBuffer = null)
{
    public enum Delegate
    {
        CPU,
        GPU
    }

    public Delegate DelegateCase { get; } = delegateCase;
    public string? ModelAssetPath { get; } = modelAssetPath;
    public byte[]? ModelAssetBuffer { get; } = modelAssetBuffer;

    private Acceleration? Acceleration =>
        DelegateCase switch
        {
            Delegate.CPU => new Acceleration
            {
                Tflite = new InferenceCalculatorOptions.Types.Delegate.Types.TfLite()
            },
            Delegate.GPU => new Acceleration
            {
                Gpu = new InferenceCalculatorOptions.Types.Delegate.Types.Gpu()
            },
            _ => null
        };

    private ExternalFile ModelAsset
    {
        get
        {
            ExternalFile file = new();

            if (ModelAssetPath != null) file.FileName = ModelAssetPath;
            if (ModelAssetBuffer != null) file.FileContent = ByteString.CopyFrom(ModelAssetBuffer);

            return file;
        }
    }

    internal BaseOptions ToProto()
    {
        return new BaseOptions
        {
            ModelAsset = ModelAsset,
            Acceleration = Acceleration
        };
    }
}