using SharpDX;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

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
        
    private void Update(EvaluationContext context)
    {
        // if (!Gradients.IsConnected)
        // {
        //     Log.Debug("Gradients count:" + Gradients.Value.Steps.Count);
        //     return;
        // }
        var gradients = new List<Gradient>(4);
        var useHorizontal = Direction.GetValue(context) == 0;
        int gradientsCount = 0;
        if (Gradients.HasInputConnections)
        {
            gradientsCount = Gradients.CollectedInputs.Count;
            if (gradientsCount == 0)
                return;
                
            var gradientsCollectedInputs = Gradients.CollectedInputs;
            foreach (var gradientsInput in gradientsCollectedInputs)
            {
                gradients.Add(gradientsInput.GetValue(context));
            }                
        }
        else
        {
            var g = Gradients.GetValue(context);
            gradientsCount = 1;
            gradients.Add(g);
        }

        var sampleCount = Resolution.GetValue(context).Clamp(1,16384);
        const int entrySizeInBytes = sizeof(float) * 4;
        var gradientSizeInBytes = sampleCount * entrySizeInBytes;
        var bufferSizeInBytes = gradientsCount * gradientSizeInBytes;
            
        try
        {
            using var dataStream = new DataStream(bufferSizeInBytes, true, true);

            var texDesc = new Texture2DDescription()
                              {
                                  Width = useHorizontal ? sampleCount : gradientsCount,
                                  Height = useHorizontal ? gradientsCount : sampleCount,
                                  ArraySize = 1,
                                  BindFlags = BindFlags.ShaderResource,
                                  Usage = ResourceUsage.Default,
                                  MipLevels = 1,
                                  CpuAccessFlags = CpuAccessFlags.None,
                                  Format = Format.R32G32B32A32_Float,
                                  SampleDescription = new SampleDescription(1, 0),
                              };

            if (useHorizontal)
            {
                foreach (var gradient in gradients)
                {
                    if (gradient == null)
                    {
                        dataStream.Seek(gradientSizeInBytes, SeekOrigin.Current);
                        continue;
                    }

                    for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                    {
                        var sampledColor = gradient.Sample((float)sampleIndex / (sampleCount - 1f));
                        dataStream.Write(sampledColor.X);
                        dataStream.Write(sampledColor.Y);
                        dataStream.Write(sampledColor.Z);
                        dataStream.Write(sampledColor.W);
                    }
                }
            }
            else
            {
                for (var sampleIndex = 0; sampleIndex < sampleCount; sampleIndex++)
                {
                    var f = sampleIndex / (sampleCount - 1f);
                    foreach (var gradient in gradients)
                    {
                        var sampledColor = gradient?.Sample(f) ?? Vector4.Zero;
                        dataStream.Write(sampledColor.X);
                        dataStream.Write(sampledColor.Y);
                        dataStream.Write(sampledColor.Z);
                        dataStream.Write(sampledColor.W);
                    }
                }
            }


            dataStream.Position = 0;
            var dataRectangles = new DataRectangle[]
                                     {
                                         new(dataPointer:dataStream.DataPointer, 
                                             pitch:useHorizontal ? gradientSizeInBytes : gradientsCount * entrySizeInBytes)
                                     };
            Utilities.Dispose(ref GradientsTexture.Value);
            GradientsTexture.Value = Texture2D.CreateTexture2D(texDesc, dataRectangles);
        }
        catch (Exception e)
        {
            Log.Warning("Unable to export gradient to texture " + e.Message, this);
        }
        Gradients.DirtyFlag.Clear();
    }

        
        
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