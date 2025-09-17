using SharpDX.Direct3D11;
using T3.Core.Utils;

namespace Lib.render._;

[Guid("de0e54c3-631b-4a01-a8a7-8cdff2e07e55")]
internal sealed class _ComputeLightOcclusions : Instance<_ComputeLightOcclusions>
{
    [Output(Guid = "D6A7B2CF-740E-4B52-8BB2-BC786F2C39AB")]
    public readonly Slot<float> Output = new();

    public _ComputeLightOcclusions()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        if (UpdateCommand.HasInputConnections && UpdateCommand.DirtyFlag.IsDirty)
        {
            // This will execute the input
            UpdateCommand.GetValue(context);
        }
        _lightIndex = LightIndex.GetValue(context).Clamp(0, 7);            
        var inputImage = InputImage.GetValue(context);
        if (inputImage == null)
        {
            return;
        }
        _textureReadAccess.InitiateReadAndConvert(inputImage, OnReadComplete);
        _textureReadAccess.Update();
    }

    private void OnReadComplete(TextureReadAccess.ReadRequestItem request)
    {
        var immediateContext = ResourceManager.Device.ImmediateContext;
        if (request.CpuAccessTexture.IsDisposed)
        {
            Log.Debug("Texture was disposed before readback was complete", this);
            return;
        }
        
        immediateContext.MapSubresource(request.CpuAccessTexture,
                                        0,
                                        0,
                                        MapMode.Read,
                                        MapFlags.None,
                                        out var sourceStream);
        
        using var stream = sourceStream;
        float result = 0;
        
        switch (request.CpuAccessTexture.Description.Format)
        {
            case Format.R32_Float:
            {
                try
                {
                    stream.Seek( sizeof(float) * _lightIndex, SeekOrigin.Begin);
                    result = stream.Read<float>();
                }
                catch (Exception e)
                {
                    Log.Warning("Failed to convert light data: " + e.Message);
                }
                break;
            }
                    
            default:
                Log.Warning($"Can't access unknown texture format {request.CpuAccessTexture.Description.Format}", this);
                break;
        }

        //Log.Debug("Result: " + result, this);
        Output.Value = result;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        _textureReadAccess.Dispose();
    }

    private int _lightIndex;
    
    
    private readonly TextureReadAccess _textureReadAccess = new();
        
    [Input(Guid = "088ddcee-1407-4cd8-85bc-6704b8ea73b1")]
    public readonly InputSlot<Command> UpdateCommand = new();

    [Input(Guid = "d2147f2d-04dd-47aa-8cab-5b588e178a1f")]
    public readonly InputSlot<Texture2D> InputImage = new();

    [Input(Guid = "2869E416-7D0B-4EF5-B25B-5794FD840848")]
    public readonly InputSlot<int> LightIndex = new();


}