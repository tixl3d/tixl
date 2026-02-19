Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 FillA;
    float4 FillB;
    float4 Background;
    float2 Size;
    float2 Offset;
    float ScaleFactor;
    float Rotate;

    float Feather;
    float RotateShapes;
    float ShapeSize;
    float BarWidth;
    float BorderWidth;
    float RowSwift;

    float RAffects_BarWidth;
    float GAffects_ShapeSize;
    float BAffects_LineRatio;
}

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

#define mod(x, y) ((x) - (y * floor(x / y)))

float box(float2 p)
{
    return max(abs(p.x), abs(p.y));
}

static float2 cellAspect = float2(1, 2);

float2 rotateDeg(float2 p, float angleInDeg)
{
    p *= cellAspect;
    float a = angleInDeg / 180 * 3.141578; // TODO: compute correct angle
    float sina = sin(-a - 3.141578 / 2);
    float cosa = cos(-a - 3.141578 / 2);

    return float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);
}

float2 rotate(float2 p, float angle)
{

    float sina = sin(-angle - 3.141578 / 2);
    float cosa = cos(-angle - 3.141578 / 2);

    return float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 orgColor = inputTexture.SampleLevel(texSampler, uv, 0.0);

    float aspectRatio = TargetWidth / TargetHeight;
    cellAspect = float2(Size.x / Size.y, 1);

    float edgeSmooth = Feather / (ScaleFactor * (Size.x + Size.y) / 2) * 100;

    float2 p = uv;
    p -= 0.5;

    // Rotate
    float rotateImageRad = (-Rotate - 90) / 180 * 3.141578;
    float rotateShapesRad = (-RotateShapes - 90) / 180 * 3.141578;

    float sina = sin(-rotateImageRad - 3.141578 / 2);
    float cosa = cos(-rotateImageRad - 3.141578 / 2);

    p.x *= aspectRatio;

    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x);

    p.x /= aspectRatio;

    // Compute raster cells
    float2 divisions = float2(TargetWidth / Size.x, TargetHeight / Size.y) / ScaleFactor;
    float2 p1 = p + Offset * float2(-1, 1) / divisions;

    float2 ppp = mod(p1, float2(1 / divisions.x, 1 / divisions.y));
    float2 pInCell = ppp * divisions;
    float2 cellId = (p1 - ppp) * divisions;

    if ((int(cellId.y + 1000.0001) + 10) % 2 == 1)
    // if (mod(cellId.y, 2.0) < 0.0001)
    {
        pInCell.y = 1 - pInCell.y;
    }
    pInCell.x = mod(pInCell.x + cellId.y * RowSwift, 1);

    float barWidth = BarWidth;
    float shapeSize = ShapeSize;
    float borderWidth = BorderWidth;

    float4 imgColorForCel = inputTexture.SampleLevel(texSampler, uv, 0.0);
    barWidth = lerp(barWidth, imgColorForCel.r, RAffects_BarWidth);
    shapeSize = lerp(shapeSize, imgColorForCel.r, GAffects_ShapeSize);
    borderWidth = lerp(borderWidth, imgColorForCel.b, BAffects_LineRatio);

    // if(RAffects_BarWidth>0 || GAffects_ShapeSize >0 || BAffects_LineRatio > 0)
    // {
    //     // Rotate position back to image space
    //     float2 gridSize = float2( 1/divisions.x, 1/divisions.y);
    //     float2 pShifted  = p1 -  gridSize/2;
    //     float2 pInCell2 = mod(pShifted, gridSize);
    //     //return float4(pInCell2,0,1);

    //     float2 pCel= p.xy-pInCell2 + gridSize/2;
    //     float sina2 = sin(-(-rotateImageRad  - 3.141578/2));
    //     float cosa2 = cos(-(-rotateImageRad - 3.141578/2));

    //     pCel.x*= aspectRatio;
    //     pCel = float2(
    //         cosa2 * pCel.x - sina2 * pCel.y,
    //         cosa2 * pCel.y + sina2 * pCel.x
    //     );
    //     pCel.x /= aspectRatio;
    //     pCel += 0.5;

    //     pCel = uv;
    //     float4 imgColorForCel = inputTexture.SampleLevel(texSampler, pCel , 0.0);
    //     //orgColor = imgColorForCel;
    //     barWidth = lerp(barWidth, imgColorForCel.r, RAffects_BarWidth);
    //     shapeSize = lerp(shapeSize, imgColorForCel.r, GAffects_ShapeSize);
    //     borderWidth = lerp(borderWidth, imgColorForCel.b, BAffects_LineRatio);
    //    // return imgColorForCel;
    // }

    // Shape 1a (left) ------------------------
    float s1a = box(rotateDeg(pInCell - float2(0, 0.5), RotateShapes));
    float s1aBorder = smoothstep(shapeSize - edgeSmooth, shapeSize + edgeSmooth, s1a - borderWidth);
    s1a = smoothstep(shapeSize - edgeSmooth, shapeSize + edgeSmooth, s1a);

    // Shape 1b (right) ------------------------
    float s1b = box(rotateDeg(pInCell - float2(1, 0.5), RotateShapes));
    float s1bBorder = smoothstep(shapeSize - edgeSmooth, shapeSize + edgeSmooth, s1b - borderWidth);
    s1b = smoothstep(shapeSize - edgeSmooth, shapeSize + edgeSmooth, s1b);

    // Shape 2 (center) ----------------------------
    float s2 = box(rotateDeg(pInCell - float2(0.5, 0.5), RotateShapes));
    float s2border = smoothstep(shapeSize - edgeSmooth, shapeSize + edgeSmooth, s2 - borderWidth);
    s2 = smoothstep(shapeSize - edgeSmooth, shapeSize + edgeSmooth, s2);

    // Center Bar ----------------------------
    // float barWidthF = saturate(BarWidth / 0.25);
    float ta = asin(barWidth * 4 + edgeSmooth / 4);
    float2 pcb = rotate(pInCell - float2(0.5, 0.5), ta);
    float centerBar = smoothstep(barWidth - edgeSmooth, barWidth + edgeSmooth, abs(pcb.x));

    // Gap Bars
    float gapBarA = smoothstep(barWidth - edgeSmooth, barWidth + edgeSmooth, abs(pcb.x - barWidth * 2));

    float gapBarB = smoothstep(barWidth - edgeSmooth, barWidth + edgeSmooth, abs(pcb.x + barWidth * 2));

    // Cell padding ---
    float cellPad = smoothstep(0.02, 0.02 + edgeSmooth, min(pInCell.x * cellAspect.x, pInCell.y));

    float fillA = 1 * lerp(1, s1a, gapBarA) * lerp(1, s1b, gapBarB) * centerBar;

    float background = 1 -
                       s1aBorder * s1bBorder * s2border;

    float4 cBorderOrBackground = lerp(Background, FillB, background);

    float4 cFill = lerp(FillA,
                        cBorderOrBackground,
                        fillA);

    return cFill;
}
