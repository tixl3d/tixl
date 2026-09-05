using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.field.space;

[Guid("c44d23c7-bfac-403d-b49e-49d00001a316")]
internal sealed class TransformField : Instance<TransformField>, IGraphNodeOp, ITransformable
{
    [Output(Guid = "9b12e766-9dcd-4c8f-83ee-2a0b78beae43")]
    public readonly Slot<ShaderGraphNode> Result = new();
    
    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Scale;
    
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public TransformField()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);

        Result.Value = ShaderNode;
        ShaderNode.AdditionalParameters = [
            new ShaderGraphNode.Parameter("float4x4", "Transform", Matrix4x4.Identity),
            new ShaderGraphNode.Parameter("float", "UniformScale", 1),
            ];
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context);
        var rotateFieldVecs = RotateFieldVecs.GetValue(context);
        if (rotateFieldVecs != _rotateFieldVecs)
        {
            _rotateFieldVecs = rotateFieldVecs;
            ShaderNode.FlagCodeChanged();
        }
        
        ShaderNode.Update(context);

        // Get parameters 
        var uniformScale = UniformScale.GetValue(context);
        var s = Scale.GetValue(context) * uniformScale;
        var r = Rotation.GetValue(context);
        var yaw = r.Y.ToRadians();
        var pitch = r.X.ToRadians();
        var roll = r.Z.ToRadians();
        var pivot = Pivot.GetValue(context);
        var t = Translation.GetValue(context);
        var objectToParentObject = GraphicsMath.CreateTransformationMatrix(scalingCenter: pivot,
                                                                           scalingRotation: Quaternion.Identity,
                                                                           scaling: new Vector3(s.X, s.Y, s.Z),
                                                                           rotationCenter: pivot,
                                                                           rotation: Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll),
                                                                           translation: new Vector3(t.X, t.Y, t.Z));

        var shearing = Shear.GetValue(context);

        var m = Matrix4x4.Identity;
        m.M12 = shearing.Y;
        m.M21 = shearing.X;
        m.M13 = shearing.Z;
        objectToParentObject = Matrix4x4.Multiply(objectToParentObject, m);

        // transpose all as mem layout in hlsl constant buffer is row based
        objectToParentObject.Transpose();
        Matrix4x4.Invert(objectToParentObject, out var invertedMatrix);

        ShaderNode.AdditionalParameters[0].Value = invertedMatrix; // This looks ugly. Should be refactored eventually
        ShaderNode.AdditionalParameters[1].Value = uniformScale; 
    }

    public ShaderGraphNode ShaderNode { get; }

    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        c.AppendCall($"""
                      p{c}.xyz = mul(float4(p{c}.xyz,1), {ShaderNode}Transform).xyz;
                       
                      """);
    }
    
    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
        if (_rotateFieldVecs)
        {
            c.AppendCall($"""
                          f{c}.xyz = mul( {ShaderNode}Transform, float4(f{c}.xyz,0)).xyz;
                          """);
        }
        c.AppendCall($"f{c}.w *= {ShaderNode}UniformScale; ");
        
    }

    private bool _rotateFieldVecs;
    
    [Input(Guid = "7248C680-7279-4C1D-B968-3864CB849C77")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();

    [Input(Guid = "3B817E6C-F532-4A8C-A2FF-A00DC926EEB2")]
    public readonly InputSlot<Vector3> Translation = new();

    [Input(Guid = "5339862D-5A18-4D0C-B908-9277F5997563")]
    public readonly InputSlot<Vector3> Rotation = new();

    [Input(Guid = "58B9DFB6-0596-4F0D-BAF6-7FB3AE426C94")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "566F1619-1DE0-4B41-B167-7FC261730D62")]
    public readonly InputSlot<float> UniformScale = new();

    [Input(Guid = "F53F3311-E1FC-418B-8861-74ADC175D5FA")]
    public readonly InputSlot<Vector3> Shear = new();

    [Input(Guid = "279730B7-C427-4924-9FDE-77EB65A3076C")]
    public readonly InputSlot<Vector3> Pivot = new();

    [Input(Guid = "DD53654B-D2F9-4656-B26C-95927F059B31")]
    public readonly InputSlot<bool> RotateFieldVecs = new();
}