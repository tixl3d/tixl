#nullable enable
using SharpDX;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.numbers.floats.process;

[Guid("55cc0f79-96c9-482e-9794-934dc0f87708")]
internal sealed class ValuesToTexture : Instance<ValuesToTexture>
{
    [Output(Guid = "f01099a0-a196-4689-9900-edac07908714")]
    public readonly Slot<Texture2D> ValuesTexture = new();

    public ValuesToTexture()
    {
        ValuesTexture.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        _valueListsTmp.Clear();

        var useHorizontal = Direction.GetValue(context) == 0;

        int listCount;
        if (Values.HasInputConnections)
        {
            listCount = Values.CollectedInputs.Count;
            if (listCount == 0)
                return;

            foreach (var vi in Values.CollectedInputs)
            {
                var v = vi.GetValue(context);
                if (v != null && v.Count > 0)
                    _valueListsTmp.Add(v);
            }

            listCount = _valueListsTmp.Count;
            if (listCount == 0)
                return;
        }
        else
        {
            var v = Values.GetValue(context);
            if (v == null || v.Count == 0)
                return;
            listCount = 1;
            _valueListsTmp.Add(v);
        }

        // Use the longest list as sampleCount
        var sampleCount = 0;
        foreach (var list in _valueListsTmp)
            sampleCount = Math.Max(sampleCount, list.Count);

        if (sampleCount == 0)
            return;

        var gain = Gain.GetValue(context);
        var pow = Pow.GetValue(context);
        if (Math.Abs(pow) < 0.001f)
            return;

        var requiredFloats = listCount * sampleCount;
        if (_uploadBuffer.Length < requiredFloats)
        {
            if (_uploadHandle.IsAllocated)
                _uploadHandle.Free();

            _uploadBuffer = new float[requiredFloats];
            _uploadHandle = GCHandle.Alloc(_uploadBuffer, GCHandleType.Pinned);
            _uploadPtr = _uploadHandle.AddrOfPinnedObject();
        }

        // Fill buffer: write row by row (each input list = one row if horizontal)
        int o = 0;
        if (useHorizontal)
        {
            foreach (var list in _valueListsTmp)
            {
                for (int i = 0; i < sampleCount; i++)
                {
                    float v = i < list.Count ? (float)Math.Pow(list[i] * gain, pow) : 0f;
                    _uploadBuffer[o++] = v;
                }
            }
        }
        else
        {
            for (int i = 0; i < sampleCount; i++)
            {
                foreach (var list in _valueListsTmp)
                {
                    float v = i < list.Count ? (float)Math.Pow(list[i] * gain, pow) : 0f;
                    _uploadBuffer[o++] = v;
                }
            }
        }

        var width  = useHorizontal ? sampleCount : listCount;
        var height = useHorizontal ? listCount   : sampleCount;

        if (ValuesTexture.Value == null ||
            ValuesTexture.Value.Description.Width  != width ||
            ValuesTexture.Value.Description.Height != height)
        {
            if (ValuesTexture.Value != null)
                Utilities.Dispose(ref ValuesTexture.Value);

            var desc = new Texture2DDescription
            {
                Width = width,
                Height = height,
                ArraySize = 1,
                BindFlags = BindFlags.ShaderResource,
                Usage = ResourceUsage.Default,
                MipLevels = 1,
                CpuAccessFlags = CpuAccessFlags.None,
                Format = Format.R32_Float,
                SampleDescription = new SampleDescription(1, 0),
            };
            ValuesTexture.Value = Texture2D.CreateTexture2D(desc);
        }

        
        const int bytesPerTexel = sizeof(float);
        var rowPitch   = width * bytesPerTexel;
        
        var slicePitch = rowPitch * height;
        var dataBox = new DataBox(_uploadPtr, rowPitch, slicePitch);
        ResourceManager.Device.ImmediateContext.UpdateSubresource(dataBox, ValuesTexture.Value, 0);

        Values.DirtyFlag.Clear();
    }
    
    // Reused, pinned upload buffer (avoid per-frame allocations)
    private float[] _uploadBuffer = [];
    private GCHandle _uploadHandle;
    private IntPtr _uploadPtr = IntPtr.Zero;

    private float[] _floatBuffer = [];
    private readonly List<List<float>> _valueListsTmp = new();

    [Input(Guid = "092C8D1F-A70E-4298-B5DF-52C9D62F8E04")]
    public readonly MultiInputSlot<List<float>> Values = new();

    [Input(Guid = "165F7E0E-6EF0-4BE1-8ED3-61ED0DB752ED")]
    public readonly InputSlot<bool> UseFullList = new();

    [Input(Guid = "CA67BFAF-EDE7-43BA-B279-FC1DDFBE2FFA")]
    public readonly InputSlot<int> RangeStart = new();

    [Input(Guid = "DB868176-D51C-41AA-BAFE-68C3E50E725E")]
    public readonly InputSlot<int> RangeEnd = new();

    [Input(Guid = "CC812393-F080-4E17-A525-15B09F8ACDD0")]
    public readonly InputSlot<float> Gain = new();

    [Input(Guid = "51545316-69FC-441F-B59F-44979E32972C")]
    public readonly InputSlot<float> Pow = new();

    [Input(Guid = "63E90D86-5AD5-4333-8B99-7F8D285C4913", MappedType = typeof(Directions))]
    public readonly InputSlot<int> Direction = new();

    private enum Directions
    {
        Horizontal,
        Vertical,
    }
}