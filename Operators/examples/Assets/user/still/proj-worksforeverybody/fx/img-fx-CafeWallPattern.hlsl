Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 Fill;    
    float4 Background;
    float4 EdgeColor;
    float2 Stretch;
    float2 Offset;
    float ScaleFactor;
    float Rotate;

    float Feather;
    float Ratio;
    float EdgeWidth;
    float RowShift;

    float RAffects_Ratio;
    float GAffects_EdgeWidth;
    float BAffects_RowShift;

    float AmplifyIllustion;
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


#define mod(x,y) ((x)-((y)*floor((x)/(y))))


float4 psMain(vsOutput psInput) : SV_TARGET
{    
    float2 uv = psInput.texCoord;
   
    float ratio = Ratio;
    float edgeWidth = EdgeWidth /2;
    float rowShift = RowShift;

    float4 imgColorForCel = inputTexture.SampleLevel(texSampler, uv , 0.0);        

    ratio += imgColorForCel.r * RAffects_Ratio;
    edgeWidth += imgColorForCel.g * GAffects_EdgeWidth;
    rowShift += imgColorForCel.b * BAffects_RowShift;

    float aspectRatio = TargetWidth/TargetHeight;
    float edgeSmooth = Feather / (ScaleFactor * (Stretch.x + Stretch.y)/2);

    float2 p = uv;
    p-= 0.5;

    // Rotate canvas
    float rotateCanvasRad = (-Rotate + 90) / 180 *3.141578;
    float sina = sin(-rotateCanvasRad - 3.141578/2);
    float cosa = cos(-rotateCanvasRad - 3.141578/2);

    p.x *=aspectRatio;
    
    p = float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x
    );

    p.x /=aspectRatio;

    // Compute raster cells
    //float2 divisions = float2(TargetWidth / Stretch.x, TargetHeight / Stretch.y) / ScaleFactor;
    float2 divisions = float2(aspectRatio,1) * 4 / (ScaleFactor * Stretch);
    float2 pCentered = (p + Offset / divisions * float2(-1,1));        
    float2 pScaled = pCentered * divisions;

    float tt = abs(mod(pScaled.y +0.5, 2.0)/2.0 - 1)-0.5;
    float sign = tt<0 ? -1:1;

    float r = sign * pScaled.x * AmplifyIllustion; 
    pScaled.y+= r;
    
    float2 pInCell = float2(
        pScaled.x,
        mod(pScaled.y, 1));

    int2 cell = (int2)(pScaled - pInCell);

    // Offset odd rows
    pInCell.x += cell.y % 2 == 0 ? -rowShift/2 : rowShift/2;

    pInCell.x = mod(pInCell.x ,1);

    float sEdge = smoothstep(edgeWidth - edgeSmooth, edgeWidth + edgeSmooth, 0.5-abs(pInCell.y-0.5));
    float sForeGroundTile = smoothstep(ratio + edgeSmooth, ratio - edgeSmooth, abs( pInCell.x - 0.5) *2 );;

    return 
    lerp(
        lerp (Background, Fill, sForeGroundTile),
        EdgeColor,
        1-sEdge);
}
