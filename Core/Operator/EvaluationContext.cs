using System;
using System.Collections.Generic;
using T3.Core.Animation;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Operator.Interfaces;
using T3.Core.Rendering;
using T3.Core.Rendering.Material;
using T3.Core.Utils;
using T3.Core.Utils.Geometry;
using Buffer = SharpDX.Direct3D11.Buffer;
using Texture2D = T3.Core.DataTypes.Texture2D;

namespace T3.Core.Operator;

public enum GizmoVisibility
{
    Inherit = -1,
    Off = 0,
    On = 1,
    IfSelected = 2,
}

public enum TransformGizmoModes
{
    None = 0,
    Select = 1,
    Move = 2,
    Rotate = 3,
    Scale = 4,
}

public sealed class EvaluationContext
{
    public EvaluationContext()
    {
        Reset();
    }
        
    /// <summary>
    /// note: generally requires setting <see cref="RequestedResolution"/> afterwards
    /// </summary>
    public void Reset()
    {
        // TODO: this should be replaced with a solution that supports multiple playback sources 
        Playback = Playback.Current;
            
        LocalTime = Playback.TimeInBars;
        LocalFxTime = Playback.FxTimeInBars;
        PointLights.Clear();
        FloatVariables.Clear();
        BoolVariables.Clear();
        IntVariables.Clear();
        ObjectVariables.Clear();
        StringVariables.Clear();
            
        PbrContextSettings.SetDefaultToContext(this);
    }

    /// <summary>
    /// This should be used for testing if operators need update.
    /// It's in bars.
    /// </summary>
    public const float TimeResolutionInBars = 0.001f;
    
    public bool HasTimeChanged(ref double lastUpdateTime)
    {
        if (Math.Abs(LocalFxTime - lastUpdateTime) < TimeResolutionInBars)
            return false;

        lastUpdateTime = LocalFxTime;
        return true;
    }

    public void SetViewFromCamera(ICamera camera)
    {
        var fov = GraphicsMath.DefaultCamFovDegrees.ToRadians();
        var aspectRatio = (float)RequestedResolution.Width / RequestedResolution.Height;
        CameraToClipSpace = GraphicsMath.PerspectiveFovRH(fov, aspectRatio, 0.01f, 1000);

        Vector3 eye = new Vector3(camera.CameraPosition.X, camera.CameraPosition.Y, camera.CameraPosition.Z);
        Vector3 target = new Vector3(camera.CameraTarget.X, camera.CameraTarget.Y, camera.CameraTarget.Z);
        Vector3 up = VectorT3.Up;
        WorldToCamera = GraphicsMath.LookAtRH(eye, target, up);

        ObjectToWorld = Matrix4x4.Identity;
    }

    public void SetDefaultCamera()
    {
        ObjectToWorld = Matrix4x4.Identity;
        WorldToCamera = GraphicsMath.LookAtRH(new Vector3(0, 0, GraphicsMath.DefaultCameraDistance), Vector3.Zero, VectorT3.Up);
        var fov =  GraphicsMath.DefaultCamFovDegrees.ToRadians();
        float aspectRatio = (float)RequestedResolution.Width / RequestedResolution.Height;
        CameraToClipSpace = GraphicsMath.PerspectiveFovRH(fov, aspectRatio, 0.01f, 1000);
    }

    private static ICamera _defaultCamera = new ViewCamera();

        
    #region timing
    public Playback Playback { get; private set; }

    /// <summary>
    /// The primary time used for user interactions and keyframe manipulation.
    /// This is where there time marker in the timeline is displayed unless overridden by operators.
    ///
    /// While evaluating the graph it can be overridden for sub graphs by <see cref="SetCommandTime"/>.
    /// </summary>
    /// <remarks>Also see <see cref="EvaluationContext"/>.<see cref="GlobalTimeForEffects"/> and .<see cref="GlobalTimeInSecs"/></remarks>
    public double LocalTime { get; set; }
        
    /// <summary>
    /// Although similar to KeyframeTime, this one keeps running in pause mode, if Keep Running is active.
    /// While evaluating the graph it can be overridden for sub graphs by <see cref="SetCommandTime"/>.
    /// </summary>
    public double LocalFxTime { get; set; }
        
    #endregion
        
    public Int2 RequestedResolution { get; set; }

    public Matrix4x4 CameraToClipSpace { get; set; } = Matrix4x4.Identity;
    public Matrix4x4 WorldToCamera { get; set; } = Matrix4x4.Identity;
    public Matrix4x4 ObjectToWorld { get; set; } = Matrix4x4.Identity;
        
        
    // Render settings
    public Buffer FogParameters { get; set; } = FogSettings.DefaultSettingsBuffer;
        
    //public PbrMaterialTextures PbrMaterialTextures { get; set; } = new();
    public PbrMaterial PbrMaterial { get; set; }
    public List<PbrMaterial> Materials { get; set; } = new(8);
        
    /// <summary>
    /// A structure that is used by SetTexture  
    /// </summary>
    public Dictionary<string, Texture2D> ContextTextures { get; set; } = new(10);
    // public Texture2D PrbPrefilteredSpecular { get; set; }
    public PointLightStack PointLights { get; } = new();
        
    /// <summary>
    /// This should be set by RenderTargets and other ops can could be directly used by SetFog.
    /// </summary>
    public System.Numerics.Vector4 BackgroundColor { get; set; } = new(0.1f, 0.1f, 0.1f, 1.0f);
        
    /// <summary>
    /// Can be set by [SetMaterial] [Group] and other ops to fade out groups  
    /// </summary>
    public System.Numerics.Vector4 ForegroundColor { get; set; } = Vector4.One;
        
    public GizmoVisibility ShowGizmos { get; set; }
    public TransformGizmoModes TransformGizmoMode { get; set; } = TransformGizmoModes.Move;
    
    #region context variables
    public Dictionary<string, bool> BoolVariables { get; } = new();
    public Dictionary<string, int> IntVariables { get; } = new();
    public Dictionary<string, float> FloatVariables { get; } = new();
    
    /// <summary>
    /// Used to store objects different from float and int
    /// </summary>
    public Dictionary<string, object> ObjectVariables { get; } = new();
    
    /// <summary>
    /// Used to store string vars
    /// </summary>
    public Dictionary<string, string> StringVariables { get; } = new();
    #endregion
    
    
    public StructuredList IteratedList { get; set; }
    public int IteratedListIndex { get; set; }
    public bool BypassCameras { get; set; }

    public LegacyParticleSystem LegacyParticleSystem;
    public ParticleSystem ParticleSystem;
}