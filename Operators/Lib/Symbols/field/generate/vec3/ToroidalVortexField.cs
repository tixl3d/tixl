using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.generate.vec3;

[Guid("5623d6b3-b8a7-4fd1-9cb4-952dc7818a83")]
internal sealed class ToroidalVortexField : Instance<ToroidalVortexField>
,IGraphNodeOp,ITransformable
{
    [Output(Guid = "fc9119f1-32ae-4b3a-93d2-f9980d9b345f")]
    public readonly Slot<ShaderGraphNode> Result = new();

    // ITransformable interface implementation (Gizmo support)
    IInputSlot ITransformable.TranslationInput => Center;
    IInputSlot ITransformable.RotationInput => null;
    IInputSlot ITransformable.ScaleInput => null;
    public Action<Instance, EvaluationContext> TransformCallback { get; set; }

    public ToroidalVortexField()
    {
        ShaderNode = new ShaderGraphNode(this);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        TransformCallback?.Invoke(this, context); // Needed for Gizmo support
        var axis = Axis.GetEnumValue<AxisTypes>(context);
        
        var templateChanged = axis != _axis;
        if (templateChanged)
        {
            _axis = axis;
            ShaderNode.FlagCodeChanged();
        }
       
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["fDecay"] = """
                              float fDecay(float dist, float falloffRadius, float rate)
                              {
                                  //float x = saturate(dist / max(falloffRadius, 1e-6));
                                  return 1.0 / (falloffRadius + pow(dist, rate));
                              }
                              """;
        
        
        // Particles spin around the torus *centerline* (major ring) and
        // can be attracted to (+) or repelled from (–) that ring.
        // Returns .xyz = velocity contribution, .w = distance to centerline (rho).
        //
        // Params
        //  radius         : major radius of the torus centerline
        //  fallOffRadius  : range for both swirl + radial influence
        //  swirlGain      : magnitude of the around-the-ring swirl (units: vel)
        //                   set 0 to disable swirl
        //  radialGain     : >0 attracts toward the ring, <0 repels away from the ring
        //  decayK         : falloff exponent (2 = quadratic, 4 = quartic, ...)
        c.Globals["fToroidalVectorField"]
            = """
              float4 fToroidalVectorField(
              float3 p, 
              float radius,
              float range,
              float swirlGain,
              float radialGain,
              float decayK)
              {
                  const float eps = 1e-6;
                  
                  // Angle around Z and basis on the centerline
                  float phi = atan2(p.y, p.x);
                  float c = cos(phi), s = sin(phi);
                  float3 e_r   = float3(c,  s, 0);  // radial in XY
                  float3 e_phi = float3(-s, c, 0);  // tangent of centerline (around Z)
                  
                  // Closest point on centerline and offset from it (minor-plane vector)
                  float3 C   = radius * e_r;
                  float3 r   = p - C;                // lies in span{e_r, e_z}
                  float  rho = length(r);
                  if (rho < eps) return float4(0,0,0,0);
                  
                  // Decay in [0,1]: 1 at rho=0, ->0 at rho >= range
                  float x = rho / max(range, eps);
                  float decay = saturate(1.0 - pow(x, decayK));
                  //decay = 1;
                  
                  // --- Swirl around the minor circle (tangent to cross-section) ---
                  // Use cross(e_phi, r) so swirl -> 0 on centerline and grows ~ rho.
                  float3 vSwirl = cross(e_phi, r);
                  // Normalize then scale by gain and decay (keeps units stable):
                  vSwirl = normalize(vSwirl) * (swirlGain * decay);
                  
                  // --- Radial attraction/repulsion toward/from the centerline ---
                  // +radialGain attracts toward the ring, -radialGain repels.
                  float3 dirToRing = -r / rho; // toward centerline
                  float3 vRadial   = dirToRing * (radialGain * decay);
                  
                  float3 v = vSwirl + vRadial;
                  return float4(v, decay);
              }
              """;
    }

    public void GetPreShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var n = ShaderNode;
        var a = _axisCodes0[(int)_axis];
        
        c.AppendCall($"f{c} = fToroidalVectorField(p{c}.{a} - {n}Center.{a}, {n}Radius, {n}Range, {n}SwirlGain, {n}RadialGain, {n}FallOffRate).{a}w;");
    }

    private readonly string[] _axisCodes0 =
        [
            "zyx",
            "xzy",
            "xyz",
        ];

    private AxisTypes _axis;

    private enum AxisTypes
    {
        X,
        Y,
        Z,
    }

    [GraphParam]
    [Input(Guid = "1efeddb4-58b1-4877-bc45-efe430f147db")]
    public readonly InputSlot<Vector3> Center = new();

    [GraphParam]
    [Input(Guid = "f2c26298-fb95-4b74-848e-28ea9ea7b724")]
    public readonly InputSlot<float> Radius = new();

    [GraphParam]
    [Input(Guid = "CDC51BA3-2989-449F-BF51-D8A642FD4647")]
    public readonly InputSlot<float> Range = new();
    
    [GraphParam]
    [Input(Guid = "634933DF-5F75-4071-A76A-6440BB3F02EF")]
    public readonly InputSlot<float> SwirlGain = new();

    [GraphParam]
    [Input(Guid = "EB2E2E6D-AF78-4B9A-A925-24CCE2164053")]
    public readonly InputSlot<float> RadialGain = new();
    
    [GraphParam]
    [Input(Guid = "f6d274ae-8502-459e-b7a9-59755e3cb21e")]
    public readonly InputSlot<float> FallOffRate = new();

    [Input(Guid = "2ed51b8f-af21-4b93-9428-45e08b9eec62", MappedType = typeof(AxisTypes))]
    public readonly InputSlot<int> Axis = new();
}