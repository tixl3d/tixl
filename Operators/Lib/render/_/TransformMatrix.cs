using T3.Core.Utils;
using T3.Core.Utils.Geometry;
// ReSharper disable InconsistentNaming

namespace Lib.render.@_;

[Guid("17324ce1-8920-4653-ac67-c211ad507a81")]
internal sealed class TransformMatrix : Instance<TransformMatrix>
{
    [Output(Guid = "751E97DE-C418-48C7-823E-D4660073A559")]
    public readonly Slot<Vector4[]> Result = new();
        
    [Output(Guid = "ECA8121B-2A7F-4ECC-9143-556DCF78BA33")]
    public readonly Slot<Vector4[]> ResultInverted = new();
        
    [Output(Guid = "6ED9317B-69B7-4041-9938-AEF2A68CA91F")]
    public readonly Slot<Matrix4x4> Matrix = new();
    
    public TransformMatrix()
    {
        Result.UpdateAction += Update;
        ResultInverted.UpdateAction += Update;
        Matrix.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var s = Scale.GetValue(context) * UniformScale.GetValue(context);
        var r = Rotation_PitchYawRoll.GetValue(context);
        float yaw = r.Y.ToRadians();
        float pitch =r.X.ToRadians();
        float roll = r.Z.ToRadians();

        var vec4 = Rotation_Quaternion.GetValue(context);
        var rotationMode = RotationMode.GetEnumValue<RotationModes>(context);
        
        var rotation = rotationMode switch
                           {
                               RotationModes.PitchYawRoll => Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll),
                               RotationModes.Quaternion   => new Quaternion(vec4.X,vec4.Y,vec4.Z,vec4.W),
                               _                          => throw new ArgumentOutOfRangeException()
                           };
        
        
        var pivot = Pivot.GetValue(context);
        var t = Translation.GetValue(context);
        var objectToParentObject = GraphicsMath.CreateTransformationMatrix(scalingCenter: pivot, 
                                                                           scalingRotation: Quaternion.Identity, 
                                                                           scaling: new Vector3(s.X, s.Y, s.Z), 
                                                                           rotationCenter: pivot,
                                                                           rotation: rotation, 
                                                                           translation: new Vector3(t.X, t.Y, t.Z));

        var shearing = Shear.GetValue(context);
        
        Matrix4x4 m = Matrix4x4.Identity;
        m.M12=shearing.Y; 
        m.M21=shearing.X; 
        m.M13=shearing.Z;             
        objectToParentObject = Matrix4x4.Multiply(objectToParentObject,m);
            
        // transpose all as mem layout in hlsl constant buffer is row based
        objectToParentObject.Transpose();
            
        if (Invert.GetValue(context))
        {
            Matrix4x4.Invert(objectToParentObject, out objectToParentObject);
        }
            
        _matrix[0] = objectToParentObject.Row1();
        _matrix[1] = objectToParentObject.Row2();
        _matrix[2] = objectToParentObject.Row3();
        _matrix[3] = objectToParentObject.Row4();
        Result.Value = _matrix;

        Matrix4x4.Invert(objectToParentObject, out var invertedMatrix);
            
        _invertedMatrix[0] = invertedMatrix.Row1();
        _invertedMatrix[1] = invertedMatrix.Row2();
        _invertedMatrix[2] = invertedMatrix.Row3();
        _invertedMatrix[3] = invertedMatrix.Row4();
        ResultInverted.Value = _invertedMatrix;

        Matrix.Value = objectToParentObject;
    }

    private Vector4[] _matrix = new Vector4[4];
    private Vector4[] _invertedMatrix = new Vector4[4];

    private enum RotationModes
    {
        PitchYawRoll,
        Quaternion,
    }    
        
    [Input(Guid = "3B817E6C-F532-4A8C-A2FF-A00DC926EEB2")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "96841452-D384-49A5-977E-F16B57DE9118", MappedType = typeof(RotationModes))]
    public readonly InputSlot<int> RotationMode = new();
    
    [Input(Guid = "5339862D-5A18-4D0C-B908-9277F5997563")]
    public readonly InputSlot<Vector3> Rotation_PitchYawRoll = new();

    [Input(Guid = "E15B0CAB-696F-44FD-B270-99F6FD26634F")]
    public readonly InputSlot<Vector4> Rotation_Quaternion = new();


    
    [Input(Guid = "58B9DFB6-0596-4F0D-BAF6-7FB3AE426C94")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "566F1619-1DE0-4B41-B167-7FC261730D62")]
    public readonly InputSlot<float> UniformScale = new();
        
    [Input(Guid = "F53F3311-E1FC-418B-8861-74ADC175D5FA")]
    public readonly InputSlot<Vector3> Shear = new();

    [Input(Guid = "279730B7-C427-4924-9FDE-77EB65A3076C")]
    public readonly InputSlot<Vector3> Pivot = new();


        
    [Input(Guid = "E19808D8-6D73-4638-B5F2-DDDDC49AD815")]
    public readonly InputSlot<bool> Invert = new();        
}