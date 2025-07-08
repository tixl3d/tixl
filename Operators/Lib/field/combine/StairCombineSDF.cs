using System.Diagnostics;
using T3.Core.DataTypes.ShaderGraph;
using T3.Core.Utils;

// ReSharper disable UnusedMember.Local

namespace Lib.field.combine;

[Guid("dda268e8-fed6-4137-b21d-56ed907e3a51")]
internal sealed class StairCombineSDF : Instance<StairCombineSDF>
                                      , IGraphNodeOp
{
    [Output(Guid = "cb491f9b-837f-4e73-9ab8-8ce1e8abb46d")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public StairCombineSDF()
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
        c.Globals["Common"] = ShaderGraphIncludes.Common;
        c.Globals["CommonHgSdf"] = ShaderGraphIncludes.CommonHgSdf;

        // Register global method
        switch (_combineMethod)
        {
            // The "Columns" flavour makes n-1 circular columns at a 45 degree angle:
            case CombineMethods.UnionColumns:
                c.Globals["fOpUnionColumns"]
                    = """
                      float fOpUnionColumns(float a, float b, float r, float n) {
                       if ((a < r) && (b < r)) {
                        float2 p = float2(a, b);
                        float columnradius = r*sqrt(2)/((n-1)*2+sqrt(2));
                        pR45(p);
                        p.x -= sqrt(2)/2*r;
                        p.x += columnradius*sqrt(2);
                        if (mod(n,2) == 1) {
                         p.y += columnradius;
                        }
                        // At this point, we have turned 45 degrees and moved at a point on the
                        // diagonal that we want to place the columns on.
                        // Now, repeat the domain along this direction and place a circle.
                        pMod1(p.y, columnradius*2);
                        float result = length(p) - columnradius;
                        result = min(result, p.x);
                        result = min(result, a);
                        return min(result, b);
                       } else {
                        return min(a, b);
                       }
                      }
                      """;
                break;

            case CombineMethods.DifferenceColumns:
            case CombineMethods.IntersectionColumns:
                c.Globals["CommonHgSdf"] = ShaderGraphIncludes.CommonHgSdf;
                c.Globals["fOpDifferenceColumns"]
                    = """
                      float fOpDifferenceColumns(float a, float b, float r, float n) {
                          a = -a;
                          float m = min(a, b);
                          //avoid the expensive computation where not needed (produces discontinuity though)
                          if ((a < r) && (b < r)) {
                          float2 p = float2(a, b);
                          float columnradius = r*sqrt(2)/n/2.0;
                          columnradius = r*sqrt(2)/((n-1)*2+sqrt(2));
                      
                          pR45(p);
                          p.y += columnradius;
                          p.x -= sqrt(2)/2*r;
                          p.x += -columnradius*sqrt(2)/2;
                      
                          if (mod(n,2) == 1) {
                          p.y += columnradius;
                          }
                          pMod1(p.y,columnradius*2);
                      
                          float result = -length(p) + columnradius;
                          result = max(result, p.x);
                          result = min(result, a);
                          return -min(result, b);
                          } else {
                          return -m;
                          }
                      }
                      """;

                c.Globals["fOpIntersectionColumns"]
                    = """
                      float fOpIntersectionColumns(float a, float b, float r, float n) {
                           return fOpDifferenceColumns(a,-b,r, n);
                      }
                      """;
                break;
            // The "Stairs" flavour produces n-1 steps of a staircase:
            // much less stupid version by paniq
            case CombineMethods.UnionStairs:
            case CombineMethods.IntersectionStairs:
            case CombineMethods.DifferenceStairs:
                c.Globals["fOpUnionStairs"]
                    = """
                      float fOpUnionStairs(float a, float b, float r, float n) {
                        float s = r/n;
                        float u = b-r;
                        return min(min(a,b), 0.5 * (u + a + abs ((mod (u - a + s, 2 * s)) - s)));
                      }

                      float fOpIntersectionStairs(float a, float b, float r, float n) {
                        return -fOpUnionStairs(-a, -b, r, n);
                      }
                      float fOpDifferenceStairs(float a, float b, float r, float n) {
                      	return -fOpUnionStairs(-a, b, r, n);
                      }
                      """;
                break;
            // first object gets a capenter-style groove cut out
            case CombineMethods.Groove:
                c.Globals["fOpGroove"] = """
                                                                      float fOpGroove(float a, float b, float ra, float rb) {
                                         	return max(a, min(a + ra, rb - abs(b)));
                                         }
                                         """;
                break;

            // first object gets a capenter-style tongue attached
            case CombineMethods.Tongue:
                c.Globals["fOpTongue"] = """
                                                                  float fOpTongue(float a, float b, float ra, float rb) {
                                         return min(a, max(a - ra, abs(b) - rb));
                                         }
                                                 
                                         """;
                break;
        }
    }

    void IGraphNodeOp.GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
        // Just pass along subcontext if not enough connected fields...
        if (ShaderNode.InputNodes.Count <= 1)
        {
            c.AppendCall("// skipping combine with single or no input...");
            return;
        }

        Debug.Assert(c.ContextIdStack.Count >= 2);

        var pc = c.ContextIdStack[^2]; // Parent context
        var n = ShaderNode;

        if (inputIndex == 0)
        {
            // Keep initial value
            c.AppendCall($"f{pc} = f{c};");
        }
        else
        {
            // Combine initial value with new value...
            switch (_combineMethod)
            {
                case CombineMethods.UnionColumns:
                    c.AppendCall($"f{pc}.w = fOpUnionColumns(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
                case CombineMethods.DifferenceColumns:
                    c.AppendCall($"f{pc}.w = fOpDifferenceColumns(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
                case CombineMethods.IntersectionColumns:
                    c.AppendCall($"f{pc}.w = fOpIntersectionColumns(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
                case CombineMethods.UnionStairs:
                    c.AppendCall($"f{pc}.w = fOpUnionStairs(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
                case CombineMethods.IntersectionStairs:
                    c.AppendCall($"f{pc}.w = fOpIntersectionStairs(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
                case CombineMethods.DifferenceStairs:
                    c.AppendCall($"f{pc}.w = fOpDifferenceStairs(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
                case CombineMethods.Groove:
                    c.AppendCall($"f{pc}.w = fOpGroove(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
                case CombineMethods.Tongue:
                    c.AppendCall($"f{pc}.w = fOpTongue(f{pc}.w, f{c}.w, {n}K, {n}Steps);");
                    break;
            }

            c.AppendCall($"f{pc}.xyz = f{pc}.w < f{c}.w ? f{pc}.xyz : f{c}.xyz;");
        }
    }

    public ShaderGraphNode ShaderNode { get; }

    private CombineMethods _combineMethod;

    private enum CombineMethods
    {
        UnionColumns,
        DifferenceColumns,
        IntersectionColumns,
        UnionStairs,
        IntersectionStairs,
        DifferenceStairs,

        Groove,
        Tongue,
    }

    [Input(Guid = "f6e33d8e-b5eb-490c-94d0-69bf7efe06f9")]
    public readonly MultiInputSlot<ShaderGraphNode> InputFields = new();

    [GraphParam]
    [Input(Guid = "2607bb4f-a9cc-4289-9404-841c12a03e96")]
    public readonly InputSlot<float> K = new();

    [GraphParam]
    [Input(Guid = "D5DC3FE0-3B7D-4AFB-A941-8E632ED80AE2")]
    public readonly InputSlot<float> Steps = new();

    [Input(Guid = "750c399c-9f8c-4bcd-8d5d-a7e26d6510c0", MappedType = typeof(CombineMethods))]
    public readonly InputSlot<int> CombineMethod = new();
}