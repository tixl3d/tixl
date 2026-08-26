using SharpDX.Direct3D11;
using T3.Core.Rendering;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using Utilities = T3.Core.Utils.Utilities;

namespace Lib.point.helper;

[Guid("e179e0da-f2cd-4cae-a39f-f89957440e74")]
internal sealed class PointToMatrix :Instance<PointToMatrix>,ICamera,ICameraPropertiesProvider
{
    [Output(Guid = "40B27356-5C28-4F2A-AD43-2F530035FFF8")]
    public readonly Slot<Vector4[]> Matrix = new();
    
    
    public PointToMatrix()
    {
        Matrix.UpdateAction += Update;
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

        //var position = p.Position;
        Vector3 forward = Vector3.Transform(Vector3.UnitZ, p.Orientation);
        
        //var target = position + forward;
        //var up = Vector3.Transform(Vector3.UnitY, p.Orientation);
        

        
        var s = p.Scale;
        // var r = Rotation_PitchYawRoll.GetValue(context);
        // float yaw = r.Y.ToRadians();
        // float pitch =r.X.ToRadians();
        // float roll = r.Z.ToRadians();

        var vec4 = p.Orientation;
        //var rotationMode = RotationMode.GetEnumValue<RotationModes>(context);

        var rotation = new Quaternion(vec4.X, vec4.Y, vec4.Z, vec4.W);
        
        
        var pivot = Vector3.Zero;
        var t = p.Position;
        var objectToParentObject = GraphicsMath.CreateTransformationMatrix(scalingCenter: pivot, 
                                                                           scalingRotation: Quaternion.Identity, 
                                                                           scaling: new Vector3(s.X, s.Y, s.Z), 
                                                                           rotationCenter: pivot,
                                                                           rotation: rotation, 
                                                                           translation: new Vector3(t.X, t.Y, t.Z));

        //var shearing = Shear.GetValue(context);
        
        // Matrix4x4 m = Matrix4x4.Identity;
        // m.M12=shearing.Y; 
        // m.M21=shearing.X; 
        // m.M13=shearing.Z;             
        // objectToParentObject = Matrix4x4.Multiply(objectToParentObject,m);
            
        // transpose all as mem layout in hlsl constant buffer is row based
        objectToParentObject.Transpose();
            
        // if (Invert.GetValue(context))
        // {
        //     Matrix4x4.Invert(objectToParentObject, out objectToParentObject);
        // }
            
        _matrix[0] = objectToParentObject.Row1();
        _matrix[1] = objectToParentObject.Row2();
        _matrix[2] = objectToParentObject.Row3();
        _matrix[3] = objectToParentObject.Row4();
        Matrix.Value = _matrix;

        // Matrix4x4.Invert(objectToParentObject, out var invertedMatrix);
        //     
        // _invertedMatrix[0] = invertedMatrix.Row1();
        // _invertedMatrix[1] = invertedMatrix.Row2();
        // _invertedMatrix[2] = invertedMatrix.Row3();
        // _invertedMatrix[3] = invertedMatrix.Row4();
        //ResultInverted.Value = _invertedMatrix;

        //Matrix.Value = objectToParentObject;        
        
        
        
        
        
        //Matrix.Value =  this;
    }
    
    private Vector4[] _matrix = new Vector4[4];
    private Vector4[] _invertedMatrix = new Vector4[4];
    
    
    [Input(Guid = "26b8e9e8-c979-4c48-92db-297a3e48139f")]
    public readonly InputSlot<StructuredList> CamPointBuffer = new();

    [Input(Guid = "4bf817b4-c35c-4584-8fbd-9bc3f7544f11")]
    public readonly InputSlot<float> SamplePos = new();
    
    
    [Input(Guid = "5063bf72-2165-45e9-9fae-e830cefdade6")]
    public readonly InputSlot<float> FieldOfView = new();
        
    [Input(Guid = "7e3d5a80-feeb-45ea-bf42-c7f83dadff46")]
    public readonly InputSlot<float> Roll = new();

    // --- offset
        
    [Input(Guid = "b213b4f0-b377-4921-9172-abca9d422a21")]
    public readonly InputSlot<Vector3> PositionOffset = new();

    [Input(Guid = "b1c695e9-08ec-46e2-a611-51774658fdd5")]
    public readonly InputSlot<bool> AlsoOffsetTarget = new();
        
    [Input(Guid = "87be4b7e-d82a-4650-9f9b-21520191a947")]
    public readonly InputSlot<Vector3> RotationOffset = new();
        
    [Input(Guid = "cbc24615-d78d-499f-8fbc-46335bdd85cf")]
    public readonly InputSlot<Vector2> LensShift = new();
        
    // --- options
        
    [Input(Guid = "a4428561-c0be-49e9-88ac-f7b48e924622")]
    public readonly InputSlot<Vector2> ClipPlanes = new();
        
    [Input(Guid = "b73edfbf-f3dc-4f4d-87a8-3207b8f0c97c")]
    public readonly InputSlot<float> AspectRatio = new();
        
    [Input(Guid = "c67e5515-c1ae-4a02-889a-1f0aae2f2de8")]
    public readonly InputSlot<Vector3> Up = new();

    public Vector3 CameraPosition { get; set; }
    public Vector3 CameraTarget { get; set; }
    public float CameraRoll { get; set; }
    public CameraDefinition CameraDefinition { get; private set; } = new();
    public Matrix4x4 WorldToCamera { get; set; }
    public Matrix4x4 LastObjectToWorld { get; set; }
    public Matrix4x4 CameraToClipSpace { get; set; }
}