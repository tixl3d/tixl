cbuffer ParamConstants : register(b0)
{
    float Displacement;
    float DisplacementOffset;
    float SampleRadius;
    float AddBlurred;

    float Twist;
    float Zoom;
    float2 Offset;

    float Rotate;
    float ShiftHue;
    float ShiftSaturation;
    float ShiftBrightness;

    float AmplifyEdge;
    float Reset;
    float2 LuminosityRange;
    float2 ChromaRange;
    float RangeClamping;

    float TwirlNoise;
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

Texture2D<float4> Image : register(t0);
Texture2D<float4> DisplaceMap : register(t1);
Texture2D<float4> FractalNoise : register(t2);
sampler texSampler : register(s0);


float3 hsb2rgb(float3 c)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
    return c.z < 0.5 ?
               c.z * 2 * lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), c.y)
                     : lerp(K.xxx, clamp(p - K.xxx, 0.0, 1.0), lerp(c.y, 0, (c.z * 2 - 1)));
}

float3 rgb2hsb(float3 c)
{
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = lerp(float4(c.bg, K.wz), float4(c.gb, K.xy), step(c.b, c.g));
    float4 q = lerp(float4(p.xyw, c.r), float4(c.r, p.yzx), step(p.x, c.r));

    float d = q.x - min(q.w, q.y);
    float e = 1.0e-10;
    return float3(
        abs(q.z + (q.w - q.y) / (6.0 * d + e)),
        d / (q.x + e),
        q.x * 0.5);
}

static float PI = 3.141578;


inline float LuminocityFromRgb(float3 c) {
    return (c.r + c.g + c.b) /3;
}

float4 psMain(vsOutput input) : SV_TARGET
{        

    



    float width, height;
    Image.GetDimensions(width, height);
    float2 uv= input.texCoord;

    // {
    //     float4 t= float4(0,0,0,1);
    //     t.x = (uv.x - 0.5) * 2;

    //     float3 hsb = rgb2hsb(float3(0.5,0.8,0.7) * t.x);
    //     if(t.x < 0) {
    //         hsb.y *= -0.62;
    //     }
    //     t.rgb = hsb;
    //     t.rgb = hsb.yyy;

    //     return t;

    // }


    // Transform...
    float aspect2 = width / height;
    float sourceAspectRatio = (float)TargetWidth / TargetHeight;

    uv += Offset;
    uv -= 0.5;

    // Rotate
    float imageRotationRad = (-Rotate - 90) / 180 * 3.141578;
    uv.x *= sourceAspectRatio;

    uv /= Zoom;

    float sina = sin(-imageRotationRad - 3.141578 / 2);
    float cosa = cos(-imageRotationRad - 3.141578 / 2);
    uv = float2(
    cosa * uv.x - sina * uv.y,
    cosa * uv.y + sina * uv.x);


    uv.x /= sourceAspectRatio;
    uv += 0.5;

    float sx = SampleRadius / width * sourceAspectRatio;
    float sy = SampleRadius / height;
    float padding =1;

    float3 cx1 = DisplaceMap.Sample(texSampler, float2(uv.x + sx, uv.y));
    float3 cx2 = DisplaceMap.Sample(texSampler, float2(uv.x - sx, uv.y));
    float3 cc = DisplaceMap.Sample(texSampler, float2(uv.x - sx, uv.y));    
    float3 cy1 = DisplaceMap.Sample(texSampler, float2(uv.x, uv.y + sy));
    float3 cy2 = DisplaceMap.Sample(texSampler, float2(uv.x, uv.y - sy));
    

    float2 d = float2(   
        LuminocityFromRgb(cx1.rgb) - LuminocityFromRgb(cx2.rgb), 
        LuminocityFromRgb(cy1.rgb) - LuminocityFromRgb(cy2.rgb));

    d.x /= sourceAspectRatio;

    float a = (d.x == 0 && d.y == 0) ? 0 : atan2(d.x, d.y) + Twist / 180 * 3.14158;

    float angleDelta = FractalNoise.Sample(texSampler, input.texCoord);
    a-= (angleDelta - 0.5) * TwirlNoise;

    float2 direction = float2(sin(a), cos(a));
    float len = length(d);


    float2 delta = len < 0.0001 ? 0: (direction * (-Displacement * len * 10 + DisplacementOffset) / float2(aspect2, 1));

    uv += delta;

    float4 c= Image.Sample(texSampler, uv);
    float3 av=  rgb2hsb((cx1+cx2+cy1+cy2+cc) / 5);
    float3 lch = rgb2hsb(c.rgb).zyx;
    float3 lchWindowCenter = lch - float3(LuminosityRange.y, ChromaRange.y,0.5) + 0.5;
    float3 s = sign(lchWindowCenter-0.5);
    lch.x += len * AmplifyEdge + ShiftBrightness + av.z * AddBlurred;
    lch.y += ShiftSaturation;
    lch.z += ShiftHue;
    
    float3 window= max(0, abs(lchWindowCenter-.5) - float3(LuminosityRange.x,ChromaRange.x,2) * 0.5) ;

    float3 windowLimiter = smoothstep(0,1, min(1,window)) * s;
    lch-= windowLimiter * RangeClamping ;
    
    c.rgb = hsb2rgb(lch.zyx);
    return c;
    
}