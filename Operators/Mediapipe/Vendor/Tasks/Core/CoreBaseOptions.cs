using Google.Protobuf;
using Mediapipe.Tasks.Core.Proto;

namespace Mediapipe.Tasks.Core;

public sealed class CoreBaseOptions(
    CoreBaseOptions.Delegate delegateCase = CoreBaseOptions.Delegate.CPU,
    string? modelAssetPath = null,
    byte[]? modelAssetBuffer = null,
    int numThreads = 0)
{
    public enum Delegate
    {
        CPU,
        GPU,
        Xnnpack
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
            // Plain TfLite-CPU is single-threaded; the Xnnpack delegate is the fast CPU path and
            // takes a thread count (0 = XNNPACK's own default).
            Delegate.Xnnpack => numThreads > 0
                ? new Acceleration
                {
                    Xnnpack = new InferenceCalculatorOptions.Types.Delegate.Types.Xnnpack { NumThreads = numThreads }
                }
                : new Acceleration
                {
                    Xnnpack = new InferenceCalculatorOptions.Types.Delegate.Types.Xnnpack()
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