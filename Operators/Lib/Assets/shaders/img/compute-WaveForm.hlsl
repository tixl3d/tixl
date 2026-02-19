RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);


cbuffer ParamConstants : register(b0)
{
    float UpperLimit;
    float SampleCount;
    float Original;
    float RGB;        
    float Lines;
    float Grayscale;
}

float IsBetween( float value, float low, float high) {
    return (value >= low && value <= high) ? 1:0;
}


[numthreads(16,16,1)]
void main(uint3 input : SV_DispatchThreadID)
{
    uint width, height;
    outputTexture.GetDimensions(width, height);

    float2 uv = (float2)input.xy/ float2(width - 1, height -1);
    float4 c = inputTexture.SampleLevel(texSampler, uv, 0.0);

    c.rgb = clamp(c.rgb, 0, 1000);
    
    c*= Original;
    float4 rgba;
    float gray;
    
    float lowBound = 0.95 * UpperLimit - uv.y * UpperLimit - 0.1;
    float highBound= 0.95 * UpperLimit - uv.y * UpperLimit - 0.1 + 1/(height) * UpperLimit;
    
    highBound += Grayscale;

    float sampleX = uv.x * 4.2 % 1.05;
    
    if ( sampleX > 1){
        //discard;
        //return;
    }
    for (int i = 0; i < SampleCount  ; ++i) {
        //float4 s = Image.Sample(samLinear, float2(sampleX, i/SampleCount));
        float4 s = inputTexture.SampleLevel(texSampler, float2(sampleX, i/SampleCount), 0.0);
        //s = clamp(s, 0, 1000);
        
        rgba.r += IsBetween( s.r, lowBound, highBound) / SampleCount;
        rgba.g += IsBetween( s.g, lowBound, highBound) / SampleCount; 
        rgba.b += IsBetween( s.b, lowBound, highBound) / SampleCount; 
        rgba.a += IsBetween( s.a, lowBound, highBound) / SampleCount; 
        
        float average=(s.r + s.g + s.b)/3 ; 
        gray += IsBetween( average, lowBound, highBound) / SampleCount; 
    }
        
    rgba = pow(rgba, 0.2) * 0.05;
    
    if( uv.x < 0.25) {
        c.r += rgba.r * RGB;
        c.gb += rgba.r * RGB * 0.5;
    }
    else if( uv.x < 0.5) {
        c.g += rgba.g * RGB;
        c.rb += rgba.g * RGB * 0.5;
    }
    else if( uv.x < 0.75) {
        c.b += rgba.b * RGB;
        c.rg += rgba.b * RGB * 0.5;
    }
    else {
        c.rgb += rgba.a * 0.5 * RGB;
    }
        
    float px = uv.x * (width+1);
    float py = uv.y * (height+1);
    float GuideAt = 0.9;
    if( IsBetween( GuideAt, lowBound, highBound)) {
        if( px % 6 > 3) {
            c.rgb+= Lines;
        }
    }
    
    if(  highBound > 1 || highBound < 0) {
        if( (px-0.5 + py-0.5) % 10 > 9) {
            c.rgb+= Lines/3;
        }
    }

    c.a = 1.0;
    c.rgb = clamp(c.rgb, 0.000001,1000);
	outputTexture[input.xy] = c;
}
