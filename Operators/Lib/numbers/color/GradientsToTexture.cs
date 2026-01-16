using SharpDX;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

// ReSharper disable ForeachCanBePartlyConvertedToQueryUsingAnotherGetEnumerator

namespace Lib.numbers.color;

[Guid("2c53eee7-eb38-449b-ad2a-d7a674952e5b")]
internal sealed class GradientsToTexture : Instance<GradientsToTexture>
{
    [Output(Guid = "7ad741ec-274d-493c-994f-1a125b96a6e9")]
    public readonly Slot<Texture2D> GradientsTexture = new();

    public GradientsToTexture()
    {
        GradientsTexture.UpdateAction += Update;
    }

    // --- GradientsToTexture.Update ---
    private void Update(EvaluationContext context)
    {
        _gradientsTmp.Clear();

        var useHorizontal = Direction.GetValue(context) == 0;

        int gradientsCount;
        if (Gradients.HasInputConnections)
        {
            gradientsCount = Gradients.CollectedInputs.Count;
            if (gradientsCount == 0)
                return;

            foreach (var gi in Gradients.CollectedInputs)
                _gradientsTmp.Add(gi.GetValue(context));
        }
        else
        {
            var g = Gradients.GetValue(context);
            if (g == null)
                return;
            gradientsCount = 1;
            _gradientsTmp.Add(g);
        }

        var sampleCount = Resolution.GetValue(context).Clamp(1, 16384);
        const int floatsPerTexel = 4;
        const int bytesPerTexel = sizeof(float) * floatsPerTexel;

        var requiredFloats = gradientsCount * sampleCount * floatsPerTexel;
        if (_uploadBuffer.Length < requiredFloats)
        {
            if (_uploadHandle.IsAllocated)
                _uploadHandle.Free();

            _uploadBuffer = new float[requiredFloats];
            _uploadHandle = GCHandle.Alloc(_uploadBuffer, GCHandleType.Pinned);
            _uploadPtr = _uploadHandle.AddrOfPinnedObject();
        }

        var o = 0;
        if (useHorizontal)
        {
            // width = sampleCount, height = gradientsCount
            foreach (var gradient in _gradientsTmp)
            {
                for (var i = 0; i < sampleCount; i++)
                {
                    var t = (float)i / (sampleCount - 1f);
                    var c = gradient?.Sample(t) ?? Vector4.Zero;
                    _uploadBuffer[o++] = c.X;
                    _uploadBuffer[o++] = c.Y;
                    _uploadBuffer[o++] = c.Z;
                    _uploadBuffer[o++] = c.W;
                }
            }
        }
        else
        {
            // width = gradientsCount, height = sampleCount
            for (var i = 0; i < sampleCount; i++)
            {
                var t = (float)i / (sampleCount - 1f);
                foreach (var gradient in _gradientsTmp)
                {
                    var c = gradient?.Sample(t) ?? Vector4.Zero;
                    _uploadBuffer[o++] = c.X;
                    _uploadBuffer[o++] = c.Y;
                    _uploadBuffer[o++] = c.Z;
                    _uploadBuffer[o++] = c.W;
                }
            }
        }

        var width = useHorizontal ? sampleCount : gradientsCount;
        var height = useHorizontal ? gradientsCount : sampleCount;

        if (GradientsTexture.Value == null ||
            GradientsTexture.Value.Description.Width != width ||
            GradientsTexture.Value.Description.Height != height)
        {
            if (GradientsTexture.Value != null)
                Utilities.Dispose(ref GradientsTexture.Value);

            var desc = new Texture2DDescription
                           {
                               Width = width,
                               Height = height,
                               ArraySize = 1,
                               BindFlags = BindFlags.ShaderResource,
                               Usage = ResourceUsage.Default,
                               MipLevels = 1,
                               CpuAccessFlags = CpuAccessFlags.None,
                               Format = Format.R32G32B32A32_Float,
                               SampleDescription = new SampleDescription(1, 0),
                           };
            GradientsTexture.Value = Texture2D.CreateTexture2D(desc);
        }

        var rowPitch = (useHorizontal ? sampleCount : gradientsCount) * bytesPerTexel;
        var slicePitch = rowPitch * height;
        var dataBox = new DataBox(_uploadPtr, rowPitch, slicePitch);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, GradientsTexture.Value, 0);

        Gradients.DirtyFlag.Clear();
    }

    // --- GradientsToTexture: fields to add ---
    private readonly List<Gradient> _gradientsTmp = new(4);
    private float[] _uploadBuffer = [];
    private GCHandle _uploadHandle;
    private IntPtr _uploadPtr = IntPtr.Zero;

    [Input(Guid = "588BE11F-D0DB-4E51-8DBB-92A25408511C")]
    public readonly MultiInputSlot<Gradient> Gradients = new();

    [Input(Guid = "1F1838E4-8502-4AC4-A8DF-DCB4CAE57DA4")]
    public readonly InputSlot<int> Resolution = new();

    [Input(Guid = "65B83219-4E3F-4A3E-A35B-705E8658CC7B", MappedType = typeof(Directions))]
    public readonly InputSlot<int> Direction = new();

    private enum Directions
    {
        Horizontal,
        Vertical,
    }
}