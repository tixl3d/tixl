using T3.Core.DataTypes.ShaderGraph;

namespace Lib.field.generate.texture;

[Guid("f17aba3c-dafc-4cab-9513-ea6bc19bcb15")]
internal sealed class SubDivPattern3d : Instance<SubDivPattern3d>
                                      , IGraphNodeOp
{
    [Output(Guid = "51decd4e-6bd4-495e-baba-80407baff141")]
    public readonly Slot<ShaderGraphNode> Result = new();

    public SubDivPattern3d()
    {
        ShaderNode = new ShaderGraphNode(this, null);
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
        c.Globals["Common"] = ShaderGraphIncludes.Common;
        c.Globals["CommonHgSdf"] = ShaderGraphIncludes.CommonHgSdf;

        c.Globals["ComputeSubdivision"] = """
                                 #include "shared/hash-functions.hlsl"

                                 struct SubDivParams
                                 {
                                     float4 GapColor;
                                     float4 ColorA;
                                     float4 ColorB;
                                 
                                     float SplitPosition;
                                     float SplitVariation;
                                     float SubdivisionThreshold;
                                     float Padding;
                                 
                                     float Feather;
                                     int UseAspectForSplit;
                                     int MaxSubdivisions;
                                 
                                     int ColorMode;
                                     int RandomSeed;
                                 };

                                 float4 ComputeSubdivision(float2 p2, SubDivParams p)
                                 {
                                     int steps = (int)clamp(p.MaxSubdivisions, 1, 30);
                                     
                                     float2 uvInCell = mod(p2,1);
                                     
                                 
                                     int step;
                                     int2 cellIds = (int2)(p2+ float2(242, 1241));
                                     int mainSeed = p.RandomSeed + cellIds.x * 2 + (cellIds.y + 3)*12311;
                                     float mainHash = hash11u(mainSeed);
                                 
                                     float2 size = 1;
                                     float phaseHashForCell = (mainHash - 0.5) * p.SplitVariation + p.SplitPosition;
                                     int seedInCell = mainSeed + p.RandomSeed;
                                     uint lastDirection = 0;
                                 
                                     [loop] for (step = 0; step < steps; ++step)
                                     {
                                         float aspect = p.UseAspectForSplit == 1 ? size.x / size.y : 1;
                                 
                                         // Split vertically
                                         if (hash11u(seedInCell) * 2 < aspect)
                                         {
                                             if (uvInCell.x < phaseHashForCell)
                                             {
                                                 uvInCell.x /= phaseHashForCell;
                                                 size.x *= phaseHashForCell;
                                                 mainSeed += (int)(phaseHashForCell + 2123u);
                                                 seedInCell *= 2;
                                             }
                                             else
                                             {
                                                 uvInCell.x = (uvInCell.x - phaseHashForCell) / (1 - phaseHashForCell);
                                                 size.x *= (1 - phaseHashForCell);
                                                 mainSeed = (int)(mainSeed + 213u) % 1251u;
                                                 seedInCell *= 3;
                                             }
                                 
                                             lastDirection = 0;
                                         }
                                         // Split horizontally
                                         else
                                         {
                                             if (uvInCell.y < phaseHashForCell)
                                             {
                                                 uvInCell.y /= phaseHashForCell;
                                                 size.y *= phaseHashForCell;
                                                 mainSeed = (int)(mainSeed + _PRIME2) % _PRIME1;
                                                 seedInCell *= 5;
                                             }
                                             else
                                             {
                                                 uvInCell.y = (uvInCell.y - phaseHashForCell) / (1 - phaseHashForCell);
                                                 size.y *= (1 - phaseHashForCell);
                                                 mainSeed = (int)(mainSeed + _PRIME1) % _PRIME2;
                                                 seedInCell *= 7;
                                             }
                                             lastDirection = 1;
                                         }
                                 
                                         float hash = hash11u(seedInCell);
                                         phaseHashForCell = (mainHash - 0.5) * p.SplitVariation + p.SplitPosition;
                                 
                                         if (hash <= p.SubdivisionThreshold)
                                             break;
                                     }
                                 
                                     float splitF = p.ColorMode == 0 ? hash11u(mainHash) : step / (float)steps;
                                 
                                     float2 dd = (uvInCell - 0.5) * size;
                                     float2 d4 = (size - abs(dd * 2)); // * float2(aspectRatio, 1);
                                 
                                     float d5 = min(d4.x, d4.y);
                                     float sGap = smoothstep(p.Padding - p.Feather, p.Padding + p.Feather, d5);
                                     return lerp(p.GapColor, 
                                     lerp(p.ColorA, p.ColorB, splitF), 
                                     sGap);
                                 }
                                 """;
    }

    public void GetPostShaderCode(CodeAssembleContext c, int inputIndex)
    {
        var n = ShaderNode;
        c.AppendCall($"""
                      SubDivParams param{n};
                      param{n}.GapColor = {n}GapColor;
                      param{n}.ColorA = {n}ColorA;
                      param{n}.ColorB = {n}ColorB;
                      param{n}.SplitPosition = {n}SplitPosition;
                      param{n}.SplitVariation = {n}SplitVariation;
                      param{n}.SubdivisionThreshold = {n}Threshold;
                      param{n}.Padding = {n}Padding;
                      param{n}.Feather = {n}Feather;
                      param{n}.UseAspectForSplit = {n}UseAspectForSplit;
                      param{n}.MaxSubdivisions = {n}MaxSubdivisions;
                      param{n}.ColorMode = {n}ColorMode;
                      param{n}.RandomSeed = {n}RandomSeed;

                      f{c}.rgb =  ComputeSubdivision(p{c}.xy, param{n}).rgb;
                      """);
    }


    [GraphParam]
    [Input(Guid = "2D22184F-4B01-47FD-B5DF-F7AACB77F023")]
    public readonly InputSlot<Vector4> GapColor = new();

    [GraphParam]
    [Input(Guid = "E95CC5A1-3425-457E-B763-18857ABA08E3")]
    public readonly InputSlot<Vector4> ColorA = new();

    [GraphParam]
    [Input(Guid = "61663A1B-2476-49ED-A375-CCE0A9E6FE03")]
    public readonly InputSlot<Vector4> ColorB = new();

    [GraphParam]
    [Input(Guid = "381AE475-D297-4399-A50B-C9FEAC597C74")]
    public readonly InputSlot<float> SplitPosition = new();

    [GraphParam]
    [Input(Guid = "A9DAC0AA-51D3-4BCF-B532-1CFDDD810675")]
    public readonly InputSlot<float> SplitVariation = new();

    [GraphParam]
    [Input(Guid = "26AAE979-60C6-4386-A33D-DF24D223ECB9")]
    public readonly InputSlot<float> Padding = new();

    [GraphParam]
    [Input(Guid = "A820D5BA-5210-4DE6-979F-E619AA117DAD")]
    public readonly InputSlot<int> UseAspectForSplit = new();

    [GraphParam]
    [Input(Guid = "6C9EF783-64FD-41DD-AEAA-21240BB2E811")]
    public readonly InputSlot<int> MaxSubdivisions = new();

    [GraphParam]
    [Input(Guid = "D1C996E3-0F2C-45AC-9ACB-52A13222CB5B")]
    public readonly InputSlot<int> ColorMode = new();

    [GraphParam]
    [Input(Guid = "7A67075A-D63C-4A54-81FE-6510A85A7096")]
    public readonly InputSlot<int> RandomSeed = new();

    [GraphParam]
    [Input(Guid = "A4B94339-63EE-4048-AFC0-88407D5C8CE3")]
    public readonly InputSlot<float> Threshold = new();

    [GraphParam]
    [Input(Guid = "b14a0607-00eb-479f-b50b-50f7f880d496")]
    public readonly InputSlot<float> Feather = new();
}