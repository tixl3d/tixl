using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.adjust;

[Guid("54f28d0a-d367-4b59-8480-5b762b8f2a9c")]
internal sealed class NoiseDisplaceSDF : Instance<NoiseDisplaceSDF>
,IGraphNodeOp
{
    [Output(Guid = "dbf31b38-5221-414c-83b1-800770fcfaa6")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public NoiseDisplaceSDF()
    {
        ShaderNode = new ShaderGraphNode(this, null, InputField);
        Result.Value = ShaderNode;
        Result.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        ShaderNode.Update(context);
    }

    public ShaderGraphNode ShaderNode { get; }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals["fSimplexNoiseDisplace"]
            = """
              float _noiseOffset_mod289(float x) {
                  return x - floor(x * (1.0 / 289.0)) * 289.0;
              }

              float3 _noiseOffset_mod289(float3 x) {
                  return x - floor(x * (1.0 / 289.0)) * 289.0;
              }

              float4 _noiseOffset_mod289(float4 x) {
                  return x - floor(x * (1.0 / 289.0)) * 289.0;
              }

              float4 _noiseOffset_permute(float4 x) {
                  return _noiseOffset_mod289(((x * 34.0) + 1.0) * x);
              }

              float4 _noiseOffset_taylorInvSqrt(float4 r) {
                  return 1.79284291400159 - 0.85373472095314 * r;
              }

              float _noiseOffset_simplexNoise3D(float3 v) {
                  const float2  C = float2(1.0 / 6.0, 1.0 / 3.0);
                  const float4  D = float4(0.0, 0.5, 1.0, 2.0);
              
                  // First corner
                  float3 i  = floor(v + dot(v, C.yyy));
                  float3 x0 = v - i + dot(i, C.xxx);
              
                  // Other corners
                  float3 g = step(x0.yzx, x0.xyz);
                  float3 l = 1.0 - g;
                  float3 i1 = min(g.xyz, l.zxy);
                  float3 i2 = max(g.xyz, l.zxy);
              
                  float3 x1 = x0 - i1 + C.xxx;
                  float3 x2 = x0 - i2 + C.yyy;
                  float3 x3 = x0 - 0.5;
              
                  // Permutations
                  i = _noiseOffset_mod289(i);
                  float4 p = _noiseOffset_permute(_noiseOffset_permute(_noiseOffset_permute(
                               i.z + float4(0.0, i1.z, i2.z, 1.0))
                             + i.y + float4(0.0, i1.y, i2.y, 1.0))
                             + i.x + float4(0.0, i1.x, i2.x, 1.0));
              
                  // Gradients
                  float4 j = p - 49.0 * floor(p * (1.0 / 49.0));  // mod(p,7*7)
              
                  float4 x_ = floor(j * (1.0 / 7.0));
                  float4 y_ = floor(j - 7.0 * x_);    // mod(j,7)
              
                  float4 x = (x_ * 2.0 + 0.5) / 7.0 - 1.0;
                  float4 y = (y_ * 2.0 + 0.5) / 7.0 - 1.0;
              
                  float4 h = 1.0 - abs(x) - abs(y);
              
                  float4 b0 = float4(x.xy, y.xy);
                  float4 b1 = float4(x.zw, y.zw);
              
                  float4 s0 = floor(b0) * 2.0 + 1.0;
                  float4 s1 = floor(b1) * 2.0 + 1.0;
                  float4 sh = -step(h, 0.0);
              
                  float4 a0 = b0.xzyw + s0.xzyw * sh.xxyy;
                  float4 a1 = b1.xzyw + s1.xzyw * sh.zzww;
              
                  float3 g0 = float3(a0.xy, h.x);
                  float3 g1 = float3(a0.zw, h.y);
                  float3 g2 = float3(a1.xy, h.z);
                  float3 g3 = float3(a1.zw, h.w);
              
                  // Normalize gradients
                  float4 norm = _noiseOffset_taylorInvSqrt(float4(dot(g0,g0), dot(g1,g1), dot(g2,g2), dot(g3,g3)));
                  g0 *= norm.x;
                  g1 *= norm.y;
                  g2 *= norm.z;
                  g3 *= norm.w;
              
                  // Mix contributions
                  float4 m = max(0.6 - float4(dot(x0,x0), dot(x1,x1), dot(x2,x2), dot(x3,x3)), 0.0);
                  m = m * m;
                  return 42.0 * dot(m * m, float4(dot(g0,x0), dot(g1,x1), dot(g2,x2), dot(g3,x3)));
              }

              float fSimplexNoiseDisplace(float3 pos, float amount, float scale, float3 offset) {
                  return _noiseOffset_simplexNoise3D(pos / scale + offset ) * amount;
              }
              """;
    }

    void IGraphNodeOp.GetPostShaderCode(CodeAssembleContext cac, int inputIndex)
    {
        var c = cac.ContextIdStack[^1];
        cac.AppendCall($"f{c}.w += fSimplexNoiseDisplace(p{c}.xyz, {ShaderNode}Amount, {ShaderNode}Scale, -{ShaderNode}Offset);");
    }

    [Input(Guid = "1799f18f-92c5-4885-b6c1-6a196eee805f")]
    public readonly InputSlot<ShaderGraphNode> InputField = new();

    [GraphParam]
    [Input(Guid = "285d7cd9-1057-4ea8-bd0b-20ff52adc562")]
    public readonly InputSlot<float> Amount = new();

    [GraphParam]
    [Input(Guid = "B7A2E12B-9E55-43A5-BE79-510F4A28A1F4")]
    public readonly InputSlot<float> Scale = new();

    [GraphParam]
    [Input(Guid = "6CD7B3CA-323C-4A69-989B-1B10009B8A90")]
    public readonly InputSlot<Vector3> Offset = new();
}