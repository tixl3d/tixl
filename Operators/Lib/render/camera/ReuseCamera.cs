namespace Lib.render.camera;

[Guid("484bec1b-e441-440a-85b4-b3865c57b4ed")]
internal sealed class ReuseCamera : Instance<ReuseCamera>
{
    [Output(Guid = "eed1f0b2-4d26-4e94-9cf1-3d2e69f3966c")]
    public readonly Slot<Command> Output = new();
        
    public ReuseCamera()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var obj = CameraReference.GetValue(context);
        if (obj == null)
        {
            Log.Warning("Camera reference is undefined", this);
            return;
        }

        try
        {
            if (obj is not ICamera camera)
            {
                Log.Warning($"Can't GetCamProperties from invalid reference type {obj}", this);
                return;
            }

            // Set properties and evaluate sub tree
            var prevWorldToCamera = context.WorldToCamera;
            var prevCameraToClipSpace = context.CameraToClipSpace;

            context.WorldToCamera = camera.WorldToCamera;
            context.CameraToClipSpace = camera.CameraToClipSpace;

            Command.GetValue(context);

            context.CameraToClipSpace = prevCameraToClipSpace;
            context.WorldToCamera = prevWorldToCamera;
        }
        catch (Exception e)
        {
            Log.Warning("Getting camera failed:" + e.Message, this);
        }
    }

    [Input(Guid = "582752a9-c68f-4312-aa71-26498c22419d")]
    public readonly InputSlot<Command> Command = new();
        
    [Input(Guid = "E3D107F3-9B81-4B8E-BDAA-35D23052C90D")]
    public readonly InputSlot<Object> CameraReference = new();
}