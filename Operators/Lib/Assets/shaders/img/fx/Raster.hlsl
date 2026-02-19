// RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 Fill;
    float4 Background;
    float2 Size;
    float2 Offset;
    float ScaleFactor;
    float Rotate;
    float DotSize;
    float LineWidth;
    float LineRatio;
    float RAffects_DotSize;
    float GAffects_LineWidth;
    float BAffects_LineRatio;
    float MixOriginal;
    float Feather;
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

#define mod(x, y) (x - y * floor(x / y))

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 orgColor = inputTexture.SampleLevel(texSampler, uv, 0.0);

    // return float4(MixOriginal, 0,0,1);

    float aspectRatio = TargetWidth / TargetHeight;
    float2 p = uv;
    p -= 0.5;

    float edgeSmooth = Feather / ScaleFactor;

    p.x *= aspectRatio;

    // Rotate
    float imageRotationRad = (-Rotate - 90) / 180 * 3.141578;

    float sina = sin(-imageRotationRad - 3.141578 / 2);
    float cosa = cos(-imageRotationRad - 3.141578 / 2);
    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);

    p.x /= aspectRatio;

    // Compute raster cells
    float2 divisions = float2(TargetWidth / Size.x, TargetHeight / Size.y) / ScaleFactor;
    float2 p1 = p + Offset * float2(-1, 1) / divisions;
    float2 pInCell = mod(p1, float2(1 / divisions.x, 1 / divisions.y));

    float dotSize = DotSize;
    float lineWidth = LineWidth;
    float lineRatio = LineRatio;

    if (RAffects_DotSize > 0 || GAffects_LineWidth > 0 || BAffects_LineRatio > 0)
    {
        // Rotate position back to image space
        float2 gridSize = float2(1 / divisions.x, 1 / divisions.y);
        float2 pShifted = p1 - gridSize / 2;
        float2 pInCell2 = mod(pShifted, gridSize);
        // return float4(pInCell2,0,1);

        float2 pCel = p.xy - pInCell2 + gridSize / 2;
        float sina2 = sin(-(-imageRotationRad - 3.141578 / 2));
        float cosa2 = cos(-(-imageRotationRad - 3.141578 / 2));

        pCel.x *= aspectRatio;
        pCel = float2(
            cosa2 * pCel.x - sina2 * pCel.y,
            cosa2 * pCel.y + sina2 * pCel.x);
        pCel.x /= aspectRatio;
        pCel += 0.5;

        float4 imgColorForCel = inputTexture.SampleLevel(texSampler, pCel, 0.0);
        // orgColor = imgColorForCel;
        dotSize = lerp(dotSize, imgColorForCel.r, RAffects_DotSize);
        lineWidth = lerp(lineWidth, imgColorForCel.g, GAffects_LineWidth);
        lineRatio = lerp(lineRatio, imgColorForCel.b, BAffects_LineRatio);
        // return imgColorForCel;
    }

    pInCell *= divisions;
    float col = 0;

    float2 pInCellCentered = abs(pInCell - 0.5) - 0.5;
    float distanceToCorner = length(pInCellCentered);

    // Draw Dots
    col += smoothstep(dotSize + edgeSmooth, dotSize - edgeSmooth, distanceToCorner);

    // Draw Lines
    float2 distanceToEdge = abs(pInCellCentered);
    float line2 = smoothstep(lineWidth / 2 + edgeSmooth, lineWidth / 2 - edgeSmooth, min(distanceToEdge.x, distanceToEdge.y));

    line2 *= LineRatio < 0.5
                 ? smoothstep(lineRatio + edgeSmooth, lineRatio - edgeSmooth, distanceToCorner)
                 : smoothstep(lineRatio - edgeSmooth, lineRatio + edgeSmooth, distanceToCorner + 0.5);

    col += line2;

    col = saturate(col);
    float4 c = lerp(Background, Fill, col);
    // col = 1-col;

    // return float4(col,col,col,1);

    float a = orgColor.a * saturate(MixOriginal) + c.a - orgColor.a * saturate(MixOriginal) * c.a;
    float3 rgb = (1.0 - c.a) * clamp(orgColor.rgb, 0, 1) + c.a * c.rgb;
    return float4(clamp(rgb, 0, 10000), clamp(a, 0, 1));

    // c.rgb = clamp(c.rgb, 0.000001,1000);
    // c.a = clamp(c.a,0,1);
    // return c;
}
