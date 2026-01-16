using SharpDX.Direct3D11;
using T3.Core.Rendering;
using T3.Core.Utils;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.point.helper;

[Guid("0cfc80fc-7b98-4cf7-982f-1aa42697bb76")]
internal sealed class CpuPointToCamera : Instance<CpuPointToCamera>
,ICamera,ICameraPropertiesProvider
{
    [Output(Guid = "A94FD64D-14D7-479F-BA9F-2BFD161CC80A")]
    public readonly Slot<Object> CamReference = new();
    
    
    public CpuPointToCamera()
    {
        CamReference.UpdateAction += Update;
    }
        
    private void Update(EvaluationContext context)
    {
        var points = CamPointBuffer.GetValue(context);
        if (points is not StructuredList<Point> pointList || pointList.NumElements == 0)
            return;

        var p = pointList.TypedElements[0];        

        // var f = SamplePos.GetValue(context).Clamp(0,points.NumElements-1);
        // var i0 = (int)f.ClampMin(0);
        // var i1 = (i0+1).ClampMax(points.NumElements-1);
        // var a = pointList.TypedElements[i0];
        // var b = pointList.TypedElements[i1];
        // var t = f - i0;
        // var p = new Point
        //             {
        //                 Position = Vector3.Lerp(a.Position, b.Position,t),
        //                 Orientation = Quaternion.Slerp(a.Orientation, b.Orientation,t),
        //             };
        
        var aspectRatio = AspectRatio.GetValue(context);
        if (aspectRatio < 0.0001f)
        {
            aspectRatio = (float)context.RequestedResolution.Width / context.RequestedResolution.Height;
        }

        var position = p.Position;
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, p.Orientation);
        
        var target = position + forward;
        var up = Vector3.Transform(Vector3.UnitY, p.Orientation);
        
        
        
        CameraDefinition = new CameraDefinition
                                {
                                    NearFarClip = ClipPlanes.GetValue(context),
                                    LensShift = LensShift.GetValue(context),
                                    PositionOffset = PositionOffset.GetValue(context),
                                    Position = position,
                                    Target = target,
                                    Up = up,
                                    AspectRatio = aspectRatio,
                                    FieldOfView = FieldOfView.GetValue(context).ToRadians(),
                                    Roll = Roll.GetValue(context),
                                    RotationOffset = RotationOffset.GetValue(context),
                                    OffsetAffectsTarget = AlsoOffsetTarget.GetValue(context)
                                };
        
        CameraDefinition.BuildProjectionMatrices(out var camToClipSpace, out var worldToCamera);

        // Set properties and evaluate sub tree
        CameraPosition = position;
        CameraTarget = target;
        WorldToCamera = worldToCamera;
        CameraToClipSpace = camToClipSpace;
        
        CamReference.Value =  this;
    }
    
    [Input(Guid = "B8DCE7D9-8316-493F-B7FB-3BDCF08C9FF8")]
    public readonly InputSlot<StructuredList> CamPointBuffer = new();

    [Input(Guid = "7B957556-29DC-451E-91BD-C859C19C7CA0")]
    public readonly InputSlot<float> SamplePos = new();
    
    
    [Input(Guid = "7BDE5A5A-CE82-4903-92FF-14E540A605F0")]
    public readonly InputSlot<float> FieldOfView = new();
        
    [Input(Guid = "764CA304-FC86-48A9-9C82-A04FAC7EADB2")]
    public readonly InputSlot<float> Roll = new();

    // --- offset
        
    [Input(Guid = "FEE19916-846F-491A-A2EE-1E7B1AC8E533")]
    public readonly InputSlot<Vector3> PositionOffset = new();

    [Input(Guid = "123396F0-62C4-43CD-8BE0-A661553D4783")]
    public readonly InputSlot<bool> AlsoOffsetTarget = new();
        
    [Input(Guid = "D4D0F046-297B-440A-AEF8-C2F0426EF4F5")]
    public readonly InputSlot<Vector3> RotationOffset = new();
        
    [Input(Guid = "AE275370-A684-42FB-AB7A-50E16D24082D")]
    public readonly InputSlot<Vector2> LensShift = new();
        
    // --- options
        
    [Input(Guid = "199D4CE0-AAB1-403A-AD42-216EF1061A0E")]
    public readonly InputSlot<Vector2> ClipPlanes = new();
        
    [Input(Guid = "F66E91A1-B991-48C3-A8C9-33BCAD0C2F6F")]
    public readonly InputSlot<float> AspectRatio = new();
        
    [Input(Guid = "E6DFBFB9-EFED-4C17-8860-9C1A1CA2FA38")]
    public readonly InputSlot<Vector3> Up = new();

    public Vector3 CameraPosition { get; set; }
    public Vector3 CameraTarget { get; set; }
    public float CameraRoll { get; set; }
    public CameraDefinition CameraDefinition { get; private set; } = new();
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }
    public Matrix4x4 CameraToClipSpace { get; set; }
}