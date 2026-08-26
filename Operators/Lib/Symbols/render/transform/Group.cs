using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;

namespace Lib.render.transform;

[Guid("a3f64d34-1fab-4230-86b3-1c3deba3f90b")]
internal sealed class Group : Instance<Group>
                    ,ITransformable
{
    [Output(Guid = "977ca2f4-cddb-4b9a-82b2-ff66453bbf9b")]
    public readonly Slot<Command> Output = new();
        
    IInputSlot ITransformable.TranslationInput => Translation;
    IInputSlot ITransformable.RotationInput => Rotation;
    IInputSlot ITransformable.ScaleInput => Scale;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public Group()
    {
        Output.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var profilingEnabled = EnableProfiling.GetValue(context);
        if (profilingEnabled)
        {
            DebugDataRecording.StartRegion(this, "Update", null, ref _profileChannel);
        }

        var startTime = Playback.RunTimeInSecs;
            
        TransformCallback?.Invoke(this, context); // this this is stupid stupid

        // Build and set transform matrix
        var s = Scale.GetValue(context) * UniformScale.GetValue(context);
        var r = Rotation.GetValue(context);
        var yaw = r.Y.ToRadians();
        var pitch = r.X.ToRadians();
        var roll = r.Z.ToRadians();
        var t = Translation.GetValue(context);
        var objectToParentObject = GraphicsMath.CreateTransformationMatrix(scalingCenter: Vector3.Zero, scalingRotation: Quaternion.Identity, scaling: new Vector3(s.X, s.Y, s.Z), rotationCenter: Vector3.Zero,
                                                                           rotation: Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll), translation: new Vector3(t.X, t.Y, t.Z));

        var forceColorUpdate = ForceColorUpdate.GetValue(context);
        var previousColor = context.ForegroundColor;
        var color = Color.GetValue(context);
            
        //color.W *= previousColor.W;     // TODO: this should be probably be controlled by an input parameter
        context.ForegroundColor *= color;
            
        var previousWorldTobject = context.ObjectToWorld;
        context.ObjectToWorld = Matrix4x4.Multiply(objectToParentObject, context.ObjectToWorld);
            
        var commands = Commands.CollectedInputs;
        if (IsEnabled.GetValue(context) && color.W > 0)
        {
            foreach (var t1 in commands)
            {
                // Do preparation if needed
                t1.Value?.PrepareAction?.Invoke(context);

                if (forceColorUpdate)
                {
                    DirtyFlag.GlobalInvalidationTick++;
                    t1.InvalidateGraph();
                }
                    
                // Execute commands
                t1.GetValue(context);

                // Cleanup after usage
                t1.Value?.RestoreAction?.Invoke(context);
            }
        }
            
        Commands.DirtyFlag.Clear();
            
        context.ForegroundColor = previousColor;
        context.ObjectToWorld = previousWorldTobject;
            
        if (profilingEnabled)
        {
            DebugDataRecording.EndRegion(_profileChannel, $"{(Playback.RunTimeInSecs - startTime) * 1000:0.0}ms");
        }
    }

    [Input(Guid = "9E961F73-1EE7-4369-9AC7-5C653E570B6F")]
    public readonly MultiInputSlot<Command> Commands = new();
        
    [Input(Guid = "9e24c5d1-191a-4c1a-b88e-03df826fffc0")]
    public readonly InputSlot<Vector3> Translation = new();
        
    [Input(Guid = "4e15eb7a-3872-4f26-93e3-1dd38bce01a5")]
    public readonly InputSlot<Vector3> Rotation = new();
        
    [Input(Guid = "7db56b09-350e-4831-9620-fe68e0617b86")]
    public readonly InputSlot<Vector3> Scale = new();

    [Input(Guid = "25eb87f8-43b6-46fa-8066-3fdc1efb5e01")]
    public readonly InputSlot<float> UniformScale = new();
        
    [Input(Guid = "83D80B87-DF2E-4A7E-8BB3-6D5F041A60E4")]
    public readonly InputSlot<bool> IsEnabled = new();

    [Input(Guid = "996BD2D7-3741-4ADE-B1B6-18EB3D884081")]
    public readonly InputSlot<Vector4> Color = new();

    [Input(Guid = "35A18838-B095-431F-A3AF-2DBA81DCC16F")]
    public readonly InputSlot<bool> ForceColorUpdate = new();

    [Input(Guid = "B864BB2D-A9FD-4E63-8D3D-B0CC8DA6A13C")]
    public readonly InputSlot<bool> EnableProfiling = new();

    private DataChannel _profileChannel;
}