#nullable enable
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

namespace Lib.field.combine;

[Guid("dbef3c50-ada6-437d-adac-62824daa9b6b")]
internal sealed class BlendSDFWithSDF : Instance<BlendSDFWithSDF>
,IGraphNodeOp,IStatusProvider
{
    [Output(Guid = "2844a064-50cf-4d03-8b2a-644ab3b15087")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public BlendSDFWithSDF()
    {
        ShaderNode = new ShaderGraphNode(this, null, FieldA, FieldB, WeightField);
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
        c.Globals[nameof(ShaderGraphIncludes.IncludeBiasFunctions)] = ShaderGraphIncludes.IncludeBiasFunctions;
        c.Globals[nameof(ShaderGraphIncludes.Common)] = ShaderGraphIncludes.Common; // For mod

        c.Globals["sdfBlendByMask"] = """
                                      // Polynomial smooth min/max (k = transition width in distance units)
                                      float sdfBlendByMaskSmin(float a, float b, float k) 
                                      {
                                          float h = saturate(0.5 + 0.5 * (b - a) / k);
                                          return lerp(b, a, h) - k * h * (1.0 - h);
                                      }
                                      
                                      // smooth intersection
                                      float sdfBlendByMaskSMax(float a, float b, float k) 
                                      { 
                                          return -sdfBlendByMaskSmin(-a, -b, k); 
                                      } 
                                      
                                      // Blend A/B by mask SDF dM. 
                                      // w = 0 → hard switch on dM=0 (exact SDF). w>0 → smooth switch of width ~w.
                                      float sdfBlendByMask(float dA, float dB, float dM, float w)
                                      {
                                          if (w <= 0.0) {
                                              // Exact: (A ∩ {dM≤0}) ∪ (B ∩ {dM≥0})
                                              float da = max(dA,  dM);   // intersect A with mask-negative halfspace
                                              float db = max(dB, -dM);   // intersect B with mask-positive halfspace
                                              return min(da, db);        // union the two clipped parts
                                          } else {
                                              // Smooth: replace max/min with smooth variants
                                              float da = sdfBlendByMaskSMax(dA,  dM,  w); // smooth intersection
                                              float db = sdfBlendByMaskSMax(dB, -dM,  w);
                                              return sdfBlendByMaskSmin(da, db, w);       // smooth union
                                          }
                                      }
                                      """;
    }

    bool IGraphNodeOp.TryBuildCustomCode(CodeAssembleContext c)
    {
        var fields = ShaderNode.InputNodes;
        if (fields is not { Count: 3 })
        {
            _lastErrorMessage = "Requires 3 input fields";
            return true;
        }

        var inputFieldA = fields[0];
        var inputFieldB = fields[1];
        var weightField = fields[2];
        if (inputFieldA == null || inputFieldB == null || weightField == null)
        {
            _lastErrorMessage = "Requires 3 input fields";
            return true;
        }

        _lastErrorMessage = string.Empty;

        inputFieldA.CollectEmbeddedShaderCode(c);
        //weightField.CollectEmbeddedShaderCode(c);

        c.AppendCall("{");
        c.Indent();

        c.PushContext(c.ContextIdStack.Count, "fieldB");
        var subFieldBContextId = c.ToString();
        inputFieldB.CollectEmbeddedShaderCode(c);
        c.PopContext();

        c.PushContext(c.ContextIdStack.Count, "weight");
        var subWeightContextId = c.ToString();
        weightField.CollectEmbeddedShaderCode(c);
        c.PopContext();
        
        c.AppendCall($"""
                      f{c}.w = sdfBlendByMask(f{c}.w, f{subFieldBContextId}.w,f{subWeightContextId}.w - {ShaderNode}Offset,{ShaderNode}Range);
                      f{c}.xyz = lerp(f{c}.xyz, f{subFieldBContextId}.xyz, smoothstep(0,1, (f{subWeightContextId}.w - {ShaderNode}Offset) / {ShaderNode}Range  ));
                      """);

        c.Unindent();
        c.AppendCall("}");

        return true;
    }
    
    #region implement status
    private string _lastErrorMessage = string.Empty;

    public IStatusProvider.StatusLevel GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    public string GetStatusMessage() => _lastErrorMessage;
    #endregion

    [Input(Guid = "eb8e21ff-6ca3-406c-b88a-f9caec9272d0")]
    public readonly InputSlot<ShaderGraphNode> FieldA = new();

    [Input(Guid = "78EC0731-E155-4C09-83B8-9B8E98A1F0AB")]
    public readonly InputSlot<ShaderGraphNode> FieldB = new();

    [Input(Guid = "9ae430e4-a09d-46d2-8aa4-fcb6a2d6fcdd")]
    public readonly InputSlot<ShaderGraphNode> WeightField = new();

    [GraphParam]
    [Input(Guid = "F245899E-AA95-4540-9600-6E761AD430EF")]
    public readonly InputSlot<float> Range = new();

    [GraphParam]
    [Input(Guid = "3301F9BB-8938-40E4-B3FC-501BCFA140A3")]
    public readonly InputSlot<float> Offset = new();
}