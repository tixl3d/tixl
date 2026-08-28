#nullable enable
using Lib.render._dx11.api;
using T3.Core.Rendering;

namespace Lib.render.shading.@_;

[Guid("843c9378-6836-4f39-b676-06fd2828af3e")]
internal sealed class _TransformsCBufferFromCam :Instance<_TransformsCBufferFromCam>, IStatusProvider{
    [Output(Guid = "FB108D2D-04B0-427D-888D-79EB7EBF1E96", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Buffer> Buffer = new();

    [Output(Guid = "8EDC2DB1-A214-4B77-A334-FA4BF1FF1AB7", DirtyFlagTrigger = DirtyFlagTrigger.Always)]
    public readonly Slot<Buffer> PreviousBuffer = new();
        
    public _TransformsCBufferFromCam()
    {
        Buffer.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        
        if (_previousBufferInitialized)
        {
            ResourceManager.SetupConstBuffer(_bufferContent, ref PreviousBuffer.Value);
            PreviousBuffer.Value.DebugName=nameof(_TransformsCBuffer);
            PreviousBuffer.DirtyFlag.Clear();
        }

        if (TryGetCamera(context, out var camera))
        {
            _bufferContent =new TransformBufferLayout(camera.CameraToClipSpace, camera.WorldToCamera, camera.LastObjectToWorld);
            
        }
        else
        {
            _bufferContent = new TransformBufferLayout(context.CameraToClipSpace, context.WorldToCamera, context.ObjectToWorld);
        }
        
        ResourceManager.SetupConstBuffer(_bufferContent, ref Buffer.Value);
        Buffer.Value.DebugName=nameof(_TransformsCBuffer);
        _previousBufferInitialized = true;
    }

    private bool TryGetCamera(EvaluationContext context, [NotNullWhen(true)] out ICameraPropertiesProvider? camera)
    {
        camera = null;
        if (!CameraReference.HasInputConnections)
        {
            CameraReference.DirtyFlag.Clear();
            return false;
        }

        var obj = CameraReference.GetValue(context);
        if (obj == null)
        {
            _lastErrorMessage = "Camera reference is undefined";
            return false;
        }

        if (obj is not ICameraPropertiesProvider camera2)
        {
            _lastErrorMessage = "Can't GetCamProperties from invalid reference type";
            return false;
        }

        camera = camera2;
        _lastErrorMessage = string.Empty;
        return true;
    }
    
    

    [Input(Guid = "A3190889-5473-4870-97CF-93E6CF94132B")]
    public readonly InputSlot<Object> CameraReference = new();

        
    private TransformBufferLayout _bufferContent;
    private bool _previousBufferInitialized;
    private string _lastErrorMessage = string.Empty;
    
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage)
            ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage() => _lastErrorMessage;
}