#nullable enable
using SharpDX;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.numbers.floats.process;

[Guid("78aee2e7-9fe6-44c4-bde3-57618007480f")]
internal sealed class ValuesToTexture2 : Instance<ValuesToTexture2>
{
    [Output(Guid = "f136acfa-4429-4121-8d36-57c6b1d26dde")]
    public readonly Slot<Texture2D> ValuesTexture = new();

    public ValuesToTexture2()
    {
        ValuesTexture.UpdateAction += Update;
    }
    
    // public static Vector2 Remap(this Vector2 value2, Vector2 inMin, Vector2 inMax, Vector2 outMin, Vector2 outMax)
    // {
    //     var factor = (value2 - inMin) / (inMax - inMin);
    //     var v = factor;
    //     return v;
    // }    
    

    private void Update(EvaluationContext context)
    {
        _valueListsTmp.Clear();

        var useHorizontal = Direction.GetValue(context) == 0;

        var inputRange = InputRange.GetValue(context);
        var outputRange = OutputRange.GetValue(context);
        var gainAndBias = GainAndBias.GetValue(context);
        var clamp = Clamp.GetValue(context);
        
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

        // var gain = Gain.GetValue(context);
        // var offset = Offset.GetValue(context);
        // var pow = Pow.GetValue(context);
        // if (Math.Abs(pow) < 0.001f)
        //     return;

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
                    o = NormalizeAndMapValue(i, list);
                }
            }
        }
        else
        {
            for (int i = 0; i < sampleCount; i++)
            {
                foreach (var list in _valueListsTmp)
                {
                    o = NormalizeAndMapValue(i, list);
                    // var orgValue = i < list.Count ? list[i] : float.NaN;
                    // var normalized = (orgValue - inputRange.X) / (inputRange.Y - inputRange.X);
                    // if (clamp)
                    // {
                    //     normalized = normalized.Clamp(0,1).ApplyGainAndBias(gainAndBias.X, gainAndBias.Y);
                    // }
                    // var v = normalized * (outputRange.Y - outputRange.X) + outputRange.X;                    
                    // _uploadBuffer[o++] = v;
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
        return;

        int NormalizeAndMapValue(int i, List<float> list)
        {
            var orgValue = i < list.Count ? list[i] : float.NaN;
            var normalized = (orgValue - inputRange.X) / (inputRange.Y - inputRange.X);
            if (clamp)
            {
                normalized = normalized.Clamp(0,1).ApplyGainAndBias(gainAndBias.X, gainAndBias.Y);
            }
            var v = normalized * (outputRange.Y - outputRange.X) + outputRange.X;
            _uploadBuffer[o++] = v;
            return o;
        }
    }
    
    // Reused, pinned upload buffer (avoid per-frame allocations)
    private float[] _uploadBuffer = [];
    private GCHandle _uploadHandle;
    private IntPtr _uploadPtr = IntPtr.Zero;

    private readonly List<List<float>> _valueListsTmp = new();

    [Input(Guid = "a80458c5-685a-434e-989b-9212324d0e14")]
    public readonly MultiInputSlot<List<float>> Values = new();
    
    [Input(Guid = "D533F4E8-F0A9-4535-B843-107C19BD3CD5")]
    public readonly InputSlot<Vector2> InputRange = new();

    [Input(Guid = "B07EAB3E-C16D-461F-BA5E-A3F34FE6F0BA")]
    public readonly InputSlot<bool> Clamp = new();
    
    [Input(Guid = "DBFD49E3-C357-4D07-963D-6DF985A2F66E")]
    public readonly InputSlot<Vector2> GainAndBias = new();

    [Input(Guid = "2D1517AA-D22D-4588-B208-774FFECBFA9D")]
    public readonly InputSlot<Vector2> OutputRange = new();
    
    [Input(Guid = "8d5cd7bb-e349-4e22-8402-6dca63728b35", MappedType = typeof(Directions))]
    public readonly InputSlot<int> Direction = new();

    private enum Directions
    {
        Horizontal,
        Vertical,
    }
}