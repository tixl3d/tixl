cbuffer ParamConstants : register(b0)
{
    float LimitDarks;
    float LimitBrights;
    float ShiftBrightness;
    float Hue;
    float Saturation;

    float DetectEdges;

    float SampleRadius;
}

// cbuffer Resolution : register(b1)
// {
//     float TargetWidth;
//     float TargetHeight;
// }

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
sampler texSampler : register(s0);





float3 rgbToHsv(float r, float g, float b)
{
    float delta, h,s, v;
    float tmp = (r < g) ? r : g;
    float min = (tmp < b) ? tmp : b;

    tmp = (r > g) ? r : g;
    float max = (tmp > b) ? tmp : b;

    v = max;				
    delta = max - min;
    if (max == min)
    {
        return float3( 0, 0, max);
    }
    else if (max != 0)
    {
        s = delta/max;
    }
    else
    {
        // r = g = b = 0		    // s = 0, v is undefined
        s = 0;
        h = 0;
        return float3(h, s, v);
    }
    if (r == max)
        h = (g - b) / delta;		// between yellow & magenta
    else if (g == max)
        h = 2 + (b - r) / delta;	// between cyan & yellow
    else
        h = 4 + (r - g) / delta;	// between magenta & cyan
    h *= 60;				        // degrees
    if (h < 0)
        h += 360;
    return float3(h,s,v);
                
}

float3 hsvToRgb( float h, float s, float v)
{
    float satR, satG, satB;
    if (h < 120.0f)
    {
        satR = (120.0f - h) / 60.0f;
        satG = h / 60.0f;
        satB = 0.0f;
    }
    else if (h < 240.0f)
    {
        satR = 0.0f;
        satG = (240.0f - h) / 60.0f;
        satB = (h - 120.0f) / 60.0f;
    }
    else
    {
        satR = (h - 240.0f) / 60.0f;
        satG = 0.0f;
        satB = (360.0f - h) / 60.0f;
    }
    satR = (satR < 1.0f) ? satR : 1.0f;
    satG = (satG < 1.0f) ? satG : 1.0f;
    satB = (satB < 1.0f) ? satB : 1.0f;

    return float3( v*(s*satR + (1.0f - s)),
                    v*(s*satG + (1.0f - s)),
                    v*(s*satB + (1.0f - s)));
                    
}


float4 psMain(vsOutput input) : SV_TARGET
{

    float width, height;
    Image.GetDimensions(width, height);
    
    float4 c=Image.Sample(texSampler, input.texCoord);
    
    float sx = SampleRadius / width;
    float sy = SampleRadius / height;

    float4 y1= Image.Sample(texSampler, float2(input.texCoord.x,       input.texCoord.y + sy));
    float4 y2= Image.Sample(texSampler, float2(input.texCoord.x,       input.texCoord.y - sy));
    
    float4 x1= Image.Sample(texSampler,  float2(input.texCoord.x + sx, input.texCoord.y));
    float4 x2= Image.Sample(texSampler,  float2(input.texCoord.x - sx, input.texCoord.y)); 

    float4 xy1= Image.Sample(texSampler, float2(input.texCoord.x+sx* 0.7 ,       input.texCoord.y + sy* 0.7 ) );
    float4 xy2= Image.Sample(texSampler, float2(input.texCoord.x+sx* 0.7 ,       input.texCoord.y - sy* 0.7 ) );
    
    float4 xy3= Image.Sample(texSampler,  float2(input.texCoord.x - sx * 0.7, input.texCoord.y + sy * 0.7));
    float4 xy4= Image.Sample(texSampler,  float2(input.texCoord.x - sx * 0.7, input.texCoord.y - sy * 0.7)); 
    
    float4 average =  (c + y1 + y2 + x1 + x2  + (xy1+ xy2 + xy3+ xy4) * 0.75    ) / 8;
    float averageGray = (average.x + average.y + average.z)/3;

    // Detect Edges
    const float increasedEdgeParmeterResolution = 100;
    float edgeDelta =  (           
                    abs(x1.r-c.r) + abs(x2.r-c.r) + abs(y1.r - c.r) +abs(y2.r - c.r) +
                    abs(x1.g-c.g) + abs(x2.g-c.g) + abs(y1.g - c.g) +abs(y2.g - c.g) +
                    abs(x1.b-c.b) + abs(x2.b-c.b) + abs(y1.b - c.b) +abs(y2.b - c.b)
                ) * DetectEdges / increasedEdgeParmeterResolution;

    // Limit value range    
    const float lowerRange = 0.1;
    const float upperRange = 0.8;

    float lowerD =  pow(clamp(lowerRange - averageGray, 0,10), 2) * LimitDarks;

    float upperD = -pow(clamp(averageGray - upperRange, 0,10), 2) * LimitBrights;
    float limitShift = lowerD + upperD;


    c.rgb += limitShift + ShiftBrightness + edgeDelta;
    
    // Shift colors
    float3 hsv = rgbToHsv(c.r, c.g, c.b);
    hsv += float3(Hue, Saturation, 0);
    c.rgb = hsvToRgb(hsv.x, hsv.y, hsv.z);

    
    c.a = clamp(c.a, 0,1);
    c.rgb = clamp(c.rgb, 0.0001, 1000);
    
    return c;
}