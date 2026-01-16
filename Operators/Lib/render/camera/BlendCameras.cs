using T3.Core.Utils;

namespace Lib.render.camera;

[Guid("e3ff58e2-847a-4c97-947c-cfbcf8f9c79d")]
internal sealed class BlendCameras : Instance<BlendCameras>, IStatusProvider, ICamera
{
    [Output(Guid = "d0a6f926-c4ed-4cc9-917d-942f8c34fd65")]
    public readonly Slot<Command> Output = new();

    [Output(Guid = "2DD98046-F80C-4CFB-8A90-AE46515EE07F")]
    public readonly Slot<object> CameraReference = new();

    public BlendCameras()
    {
        Output.UpdateAction += Update;
        CameraReference.UpdateAction += Update;
        
        CameraReference.Value = this;

    }

    private void Update(EvaluationContext context)
    {
        try
        {
            var cameraInputs = CameraReferences.GetCollectedTypedInputs();
            var cameraCount = cameraInputs.Count;

            var floatIndex = Index.GetValue(context).Clamp(0, cameraCount - 1.0001f);
            var index = (int)floatIndex;

            ICamera camA;
            ICamera camB;
                
            CameraReferences.DirtyFlag.Clear();
            if (cameraCount == 0)
            {
                _lastErrorMessage = "No cameras connected?";
                return;
            }

            if (cameraCount == 1)
            {
                if (cameraInputs[0].GetValue(context) is ICamera cam)
                {
                    camA = cam;
                    camB = cam;
                }
                else
                {
                    _lastErrorMessage = "That's not a camera";
                    return;
                }
            }
            else
            {
                if (cameraInputs[index].GetValue(context) is ICamera camA_
                    && cameraInputs[index + 1].GetValue(context) is ICamera camB_)
                {
                    _lastErrorMessage = null;
                    camA = camA_;
                    camB = camB_;
                }
                else
                {
                    _lastErrorMessage = "Can't access cameras.";
                    return;
                }
            }
                
            if (context.BypassCameras)
            {
                Command.GetValue(context);
                return;
            }

            var blend = floatIndex - index;
            _blendedCamDef = CameraDefinition.Blend(camA.CameraDefinition, camB.CameraDefinition, blend);

            _blendedCamDef.BuildProjectionMatrices(out var camToClipSpace, out var worldToCamera);

            WorldToCamera = worldToCamera;
            CameraToClipSpace = camToClipSpace;
            
            // Set properties and evaluate sub-tree
            var prevWorldToCamera = context.WorldToCamera;
            var prevCameraToClipSpace = context.CameraToClipSpace;

            context.WorldToCamera = worldToCamera;
            context.CameraToClipSpace = camToClipSpace;

            Command.GetValue(context);

            context.CameraToClipSpace = prevCameraToClipSpace;
            context.WorldToCamera = prevWorldToCamera;

        }
        catch (Exception e)
        {
            _lastErrorMessage = "Didn't work " + e.Message;
        }
    }



    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _lastErrorMessage;
    }

    #region Implement ICamera
    
    public Vector3 CameraPosition { get => _blendedCamDef.Position; set {} }
    public Vector3 CameraTarget { get=> _blendedCamDef.Target; set { } }
    public float CameraRoll { get => _blendedCamDef.Roll; set { } }
    public CameraDefinition CameraDefinition => _blendedCamDef;
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 CameraToClipSpace { get; set; }
    #endregion
    
    private string _lastErrorMessage;
    private CameraDefinition _blendedCamDef;


    [Input(Guid = "C7EE5D97-86C1-442F-91D0-B60E6CFE24C7")]
    public readonly InputSlot<Command> Command = new();

    [Input(Guid = "FF2ED90B-38BD-4BA8-AF07-23BE87EABCC3")]
    public readonly MultiInputSlot<Object> CameraReferences = new();

    [Input(Guid = "3B71FDBF-CB2D-45F1-84DD-7AC66763E6AE")]
    public readonly InputSlot<float> Index = new();
}
