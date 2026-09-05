using System.Diagnostics;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

// ReSharper disable UnusedMember.Local

namespace Lib.field.combine;

[Guid("82270977-07b5-4d86-8544-5aebc638d46c")]
internal sealed class CombineSDF : Instance<CombineSDF>
,IGraphNodeOp
{
    [Output(Guid = "db0bbde0-18b6-4c53-8cf7-a294177d2089")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public CombineSDF()
    {
        ShaderNode = new ShaderGraphNode(this, InputFields);
        Result.UpdateAction += Update;
        Result.Value = ShaderNode;
    }

    private void Update(EvaluationContext context)
    {
        var combineMethod = CombineMethod.GetEnumValue<CombineMethods>(context);
        if (combineMethod != _combineMethod)
        {
            _combineMethod = combineMethod;
            ShaderNode.FlagCodeChanged();
        }

        ShaderNode.Update(context);

        // Get all parameters to clear operator dirty flag
        InputFields.DirtyFlag.Clear();
    }

    void IGraphNodeOp.AddDefinitions(CodeAssembleContext c)
    {
        c.Globals[nameof(ShaderGraphIncludes.Common)] = ShaderGraphIncludes.Common;
        c.Globals[nameof(ShaderGraphIncludes.CommonHgSdf)] = ShaderGraphIncludes.CommonHgSdf;
        c.Globals[nameof(ShaderGraphIncludes.GetColorBlendFactor)] = ShaderGraphIncludes.GetColorBlendFactor;
        
        // Register global method
        switch (_combineMethod)
        {
            case CombineMethods.UnionSmooth:
                c.Globals["fOpSmoothUnion"]
                    = """
                      float fOpSmoothUnion(float a, float b, float k) {
                          float h = max(k - abs(a - b), 0.0);
                          return min(a, b) - (h * h) / (4.0 * k);
                      };
                      """;
                break;

            case CombineMethods.UnionChamfer:
                c.Globals["fOpUnionChamfer"]
                    = """
                      // The "Chamfer" flavour makes a 45-degree chamfered edge (the diagonal of a square of size <r>):
                      float fOpUnionChamfer(float a, float b, float r) {
                          return min(min(a, b), (a - r + b)*sqrt(0.5));
                      }
                      """;
                break;
            case CombineMethods.IntersectChamfer:
            case CombineMethods.CutOutChamfer:
                c.Globals["fOpIntersectionChamfer"]
                    = """
                      // Intersection has to deal with what is normally the inside of the resulting object
                      // when using union, which we normally don't care about too much. Thus, intersection
                      // implementations sometimes differ from union implementations.
                      float fOpIntersectionChamfer(float a, float b, float r) {
                          return max(max(a, b), (a + r + b)*sqrt(0.5));
                      }
                      """;

                c.Globals["fOpDifferenceChamfer"]
                    = """
                      // Difference can be built from Intersection or Union:
                      float fOpDifferenceChamfer (float a, float b, float r) {
                          return fOpIntersectionChamfer(a, -b, r);
                      }
                      """;
                break;
            case CombineMethods.UnionRound:
                c.Globals["fOpUnionRound"]
                    = """
                       // The "Round" variant uses a quarter-circle to join the two objects smoothly:
                       float fOpUnionRound(float a, float b, float r) {
                           float2 u = max(float2(r - a,r - b), 0);
                           return max(r, min (a, b)) - length(u);
                       }
                      """;
                break;

            case CombineMethods.IntersectRound:
            case CombineMethods.CutOutRound:
                c.Globals["fOpIntersectionRound"]
                    = """
                      float fOpIntersectionRound(float a, float b, float r) {
                          float2 u = max(float2(r + a,r + b), 0);
                          return min(-r, max (a, b)) + length(u);
                      }      
                      """;
                
                c.Globals["fOpDifferenceRound"]
                    = """
                      float fOpDifferenceRound (float a, float b, float r) {
                          return fOpIntersectionRound(a, -b, r);
                      }
                      """;
                break;
            
            // Similar to fOpUnionRound, but more lipschitz-y at acute angles
            // (and less so at 90 degrees). Useful when fudging around too much
            // by MediaMolecule, from Alex Evans' siggraph slides
            case CombineMethods.UnionSoft:
                c.Globals["fOpUnionSoft"] = """
                                            float fOpUnionSoft(float a, float b, float r) {
                                            	float e = max(r - abs(a - b), 0);
                                            	return min(a, b) - e*e*0.25/r;
                                            }
                                            """;
                break;

            // produces a cylindrical pipe that runs along the intersection.
            // No objects remain, only the pipe. This is not a boolean operator.
            case CombineMethods.Pipe:
                c.Globals["fOpPipe"] = """
                                       float fOpPipe(float a, float b, float r) {
                                       	return length(float2(a, b)) - r;
                                       }
                                       """;
                break;
            // first object gets a v-shaped engraving where it intersects the second
            case CombineMethods.Engrave:
                c.Globals["fOpEngrave"] = """
                                          float fOpEngrave(float a, float b, float r) {
                                          	return max(a, (a + r - abs(b))*sqrt(0.5));
                                          }
                                          """;

                break;
        }
    }

     void IGraphNodeOp.GetPostShaderCode(CodeAssembleContext cac, int inputIndex)
    {
        // Just pass along subcontext if not enough connected fields...
        if (ShaderNode.InputNodes.Count <= 1)
        {
            cac.AppendCall("// skipping combine with single or no input...");
            return;
        }

        Debug.Assert(cac.ContextIdStack.Count >= 2);

        var contextId = cac.ContextIdStack[^2];
        var subContextId = cac.ContextIdStack[^1];

        if (inputIndex == 0)
        {
            // Keep initial value
            cac.AppendCall($"f{contextId} = f{subContextId};");
            
        }
        else
        {
            cac.AppendCall($"f{contextId}.rgb = lerp(f{contextId}.rgb, f{subContextId}.rgb, GetColorBlendFactor(f{contextId}.w, f{subContextId}.w, {ShaderNode}K ));");
            
            // Combine initial value with new value...
            switch (_combineMethod)
            {
                case CombineMethods.Union:
                    cac.AppendCall($"f{contextId}.w = min(f{contextId}.w, f{subContextId}.w);");
                    break;
                case CombineMethods.Intersect:
                    cac.AppendCall($"f{contextId}.w = max(f{contextId}.w, f{subContextId}.w);");
                    break;
                case CombineMethods.UnionSmooth:
                    cac.AppendCall($"f{contextId}.w = fOpSmoothUnion(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
                case CombineMethods.CutOut:
                    cac.AppendCall($"f{contextId}.w = max(f{contextId}.w, -f{subContextId}.w);");
                    break;

                case CombineMethods.UnionChamfer:
                    cac.AppendCall($"f{contextId}.w = fOpUnionChamfer(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
                case CombineMethods.IntersectChamfer:
                    cac.AppendCall($"f{contextId}.w = fOpIntersectionChamfer(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
                
                case CombineMethods.IntersectRound:
                    cac.AppendCall($"f{contextId}.w = fOpIntersectionRound(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
                case CombineMethods.CutOutChamfer:
                    cac.AppendCall($"f{contextId}.w = fOpDifferenceChamfer(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
                case CombineMethods.UnionRound:
                    cac.AppendCall($"f{contextId}.w = fOpUnionRound(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;

                case CombineMethods.CutOutRound:
                    cac.AppendCall($"f{contextId}.w = fOpDifferenceRound(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;

                case CombineMethods.UnionSoft:
                    cac.AppendCall($"f{contextId}.w = fOpUnionSoft(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
                case CombineMethods.Pipe:
                    cac.AppendCall($"f{contextId}.w = fOpPipe(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
                case CombineMethods.Engrave:
                    cac.AppendCall($"f{contextId}.w = fOpEngrave(f{contextId}.w, f{subContextId}.w, {ShaderNode}K);");
                    break;
            }
        }
    }

    public ShaderGraphNode ShaderNode { get; }

    private CombineMethods _combineMethod;

    private enum CombineMethods
    {
        Union,
        UnionSoft,
        UnionRound,
        UnionChamfer,
        UnionSmooth,
        
        CutOut,
        CutOutRound,
        CutOutChamfer,

        Intersect,
        IntersectRound,
        IntersectChamfer,

        Pipe,
        Engrave,

    }

    [Input(Guid = "7248C680-7279-4C1D-B968-3864CB849C77")]
    public readonly MultiInputSlot<ShaderGraphNode> InputFields = new();

    [GraphParam]
    [Input(Guid = "9E4F5916-722D-4C4B-B1CA-814958A5B836")]
    public readonly InputSlot<float> K = new();

    [Input(Guid = "4648E514-B48C-4A98-A728-3EBF9BCFA0B7", MappedType = typeof(CombineMethods))]
    public readonly InputSlot<int> CombineMethod = new();
}