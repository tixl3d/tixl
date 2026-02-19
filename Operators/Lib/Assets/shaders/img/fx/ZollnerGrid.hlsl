Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 Fill;    
    float4 Background;
    float2 Stretch;
    float2 Offset;
    float ScaleFactor;
    float Rotate;

    float Feather;
    float HookRotation;
    float HookLength;
    float HookWidth;
    float BarWidth;
    float RowSwift;

    float RAffects_BarWidth;
    float GAffects_HookLength;
    float BAffects_HookRotation;

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


#define mod(x,y) ((x)-(y*floor(x/y)))

float box(float2 p) {
    return max(abs(p.x), abs(p.y));
}

static float2 cellAspect = float2(1,2);

float2 rotateDeg(float2 p, float angleInDeg) 
{
    p *= cellAspect;
    float a= angleInDeg / 180 * 3.141578; // TODO: compute correct angle
    float sina = sin(-a - 3.141578/2);
    float cosa = cos(-a - 3.141578/2);    

    return float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x
    );    
}

float2 rotate(float2 p, float angle) {
    
    float sina = sin(-angle - 3.141578/2);
    float cosa = cos(-angle - 3.141578/2);    

    return float2(
        cosa * p.x - sina * p.y,
        cosa * p.y + sina * p.x
    );    
}


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;


    float barWidth = BarWidth / 2;
    float hookLength = HookLength /2;
    float hookWidth = HookWidth / 2;
    float hookRotation = HookRotation + 90;

    float4 imgColorForCel = inputTexture.SampleLevel(texSampler, uv , 0.0);        

    barWidth += imgColorForCel.r * RAffects_BarWidth;

    hookLength += imgColorForCel.r * GAffects_HookLength;
    hookWidth += imgColorForCel.r * GAffects_HookLength;
    hookRotation += imgColorForCel.b * BAffects_HookRotation;
    
    float aspectRatio = TargetWidth/TargetHeight;
    float edgeSmooth = Feather / (ScaleFactor * (Stretch.x + Stretch.y)/2);

    float2 p = uv;
    p-= 0.5;

    // Rotate canvas
    float rotateCanvasRad = (-Rotate) / 180 *3.141578;
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
        
    //float2 y = pCentered.y * divisions.y;
    float2 pScaled = pCentered * divisions;
    float2 pInCell = float2(
        pCentered.x * divisions.x,
        mod(pScaled.y, 1));
    //float pInCellX = pCentered.x * divisions.x;
    int2 cell = (int2)(pScaled - pInCell);

    if(cell.y % 2 == 0) {

        pInCell.y = (1-pInCell.y);
        pInCell.x += 0.5;// / divisions.x;
    }

    //float2 p1 = float2(pInCellX, pInCellY);
    float2 p1 = pInCell;
    //return float4(mod(p1,0.3),0,1);
    
    pInCell.y -= p1.x * AmplifyIllustion;

    //return float4(p1,0,1);

    // Sheer x
    {        
        float a= hookRotation / 180 * 3.141578;
        float sina = sin(-a - 3.141578/2);
        float cosa = cos(-a - 3.141578/2);

        //float px = (p1.x);
        p1.x = (cosa * p1.x - sina * pInCell.y) / sin(-a);
        p1.x +=cell.y * RowSwift;
    }

    pInCell.x = mod(p1.x ,1);
    //float2 pInCell = float2( mod(p1.x ,1), pInCellY);
    
    float sHookLine = smoothstep(hookWidth + edgeSmooth * aspectRatio , hookWidth  - edgeSmooth * aspectRatio , abs(pInCell.x - 0.5));
    float sHookBar =  smoothstep(hookLength + edgeSmooth, hookLength - edgeSmooth, abs(pInCell.y - 0.5));
    float sCenterBar =  smoothstep(barWidth + edgeSmooth, barWidth  - edgeSmooth, abs(pInCell.y - 0.5));

    float s =  max( min(sHookLine, sHookBar), sCenterBar);
    return lerp(Background, Fill,s);    
}
