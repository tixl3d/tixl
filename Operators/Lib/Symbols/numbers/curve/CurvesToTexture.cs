using SharpDX;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.numbers.curve;

[Guid("ab511978-bad5-4b69-90b2-c028447fe9f7")]
internal sealed class CurvesToTexture : Instance<CurvesToTexture>
{
    [Output(Guid = "0322FFC8-84BD-4AA3-A59E-DEF5B212D4A1")]
    public readonly Slot<Texture2D> CurveTexture = new();

    public CurvesToTexture()
    {
        CurveTexture.UpdateAction += Update;
    }

    private readonly List<Curve> _curves = new(4);

    private void Update(EvaluationContext context)
    {
        _curves.Clear();

        var useGrayScale = ExportGrayScale.GetValue(context);
        var useHorizontal = Direction.GetValue(context) == 0;
        var sampleCount = SampleSize.GetValue(context).Clamp(1, 16384);

        if (Curves.HasInputConnections)
        {
            foreach (var curveInput in Curves.CollectedInputs)
            {
                var curve = curveInput.GetValue(context);
                if (curve == null)
                    continue;

                _curves.Add(curve);
            }
            Curves.DirtyFlag.Clear();
        }
        else
        {
            var c = Curves.GetValue(context);
            if (c != null)
                _curves.Add(c);
        }

        var curveCount = _curves.Count;
        if (curveCount == 0)
            return;

        //const int sampleCount = 256;
        var grayScaleSizeFactor = useGrayScale ? 4 : 1;
        var entrySizeInBytes = sizeof(float) * grayScaleSizeFactor;
        var curveSizeInBytes = sampleCount * entrySizeInBytes;
        int bufferSizeInBytes = curveCount * curveSizeInBytes;

        try
        {
            //var format = TextureFormat.GetValue(context);

            using var dataStream = new DataStream(bufferSizeInBytes, true, true);

            var texDesc = new Texture2DDescription()
                              {
                                  Width = useHorizontal ? sampleCount : curveCount,
                                  Height = useHorizontal ? curveCount : sampleCount,
                                  ArraySize = 1,
                                  BindFlags = BindFlags.ShaderResource,
                                  Usage = ResourceUsage.Default,
                                  MipLevels = 1,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  Format = useGrayScale ? Format.R32G32B32A32_Float : Format.R32_Float,
                                  SampleDescription = new SampleDescription(1, 0),
                              };

            if (useHorizontal)
            {
                foreach (var curve in _curves)
                {
                    for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        if (useGrayScale)
                        {
                            var sampledFloatValue = (float)curve.GetSampledValue((float)sampleIndex / sampleCount);
                            dataStream.Write(sampledFloatValue);
                            dataStream.Write(sampledFloatValue);
                            dataStream.Write(sampledFloatValue);
                            dataStream.Write(1f);
                        }
                        else
                        {
                            dataStream.Write((float)curve.GetSampledValue((float)sampleIndex / sampleCount));
                        }
                    }
                }
            }
            else
            {
                for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    foreach (var curve in _curves)
                    {
                        if (useGrayScale)
                        {
                            var sampledFloatValue = (float)curve.GetSampledValue((float)sampleIndex / sampleCount);
                            dataStream.Write(sampledFloatValue);
                            dataStream.Write(sampledFloatValue);
                            dataStream.Write(sampledFloatValue);
                            dataStream.Write(1f);
                        }
                        else
                        {
                            dataStream.Write((float)curve.GetSampledValue((float)sampleIndex / sampleCount));
                        }
                    }
                }
            }

            dataStream.Position = 0;
            var dataRectangles = new DataRectangle[]
                                     {
                                         new(dataPointer: dataStream.DataPointer, 
                                             pitch: useHorizontal ? curveSizeInBytes : curveCount * entrySizeInBytes
                                            )
                                     };
            Utilities.Dispose(ref CurveTexture.Value);
            CurveTexture.Value = Texture2D.CreateTexture2D(texDesc, dataRectangles);
        }
        catch (Exception e)
        {
            Log.Warning("Unable to export curve to texture " + e.Message, this);
        }
    }

    [Input(Guid = "83c5a68a-2685-4506-8d79-d0db7d739102")]
    public readonly MultiInputSlot<Curve> Curves = new();

    [Input(Guid = "74A9437B-A3CD-46C8-AA14-7C45FA629933")]
    public readonly InputSlot<bool> ExportGrayScale = new();

    [Input(Guid = "63C7F648-7E02-4E0B-AAEE-3A4C6869D75F")]
    public readonly InputSlot<int> SampleSize = new();
        
    [Input(Guid = "63E90D86-5AD5-4333-8B99-7F8D285C4913", MappedType = typeof(Directions))]
    public readonly InputSlot<int> Direction = new();
        
    private enum Directions
    {
        Horizontal,
        Vertical,
    }
}