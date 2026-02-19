cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float2 Target;
    float Width;
    float Aspect;
    float Shape;
    float Shape2;
    float Time;

    float Noise;
    float Complexity;
    float SegmentFill;

    float4 FillColor;
    float4 Background;

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

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);



// float sdBox( in float2 p, in float2 b )
// {
//     float2 d = abs(p)-b;
//     return length(
//         max(d,float2(0,0))) + min(max(d.x,d.y), 
//         0.0);
// }

//#define PI    3.14159265358979323846
//#define TAU (PI*2.0)
// float PI = 3.14159265358979323846;
// float TAU = 3.14159265358979323846*2;

static float PI = 3.141592653;
static float TAU = (3.1415926535 * 2);

float mod(float x, float y) {
    return (x - y * floor(x / y));
} 

// --- Spectral Zucconi --------------------------------------------
// By Alan Zucconi
// Based on GPU Gems: https://developer.nvidia.com/sites/all/modules/custom/gpugems/books/GPUGems/gpugems_ch08.html
// But with values optimised to match as close as possible the visible spectrum
// Fits this: https://commons.wikimedia.org/wiki/File:Linear_visible_spectrum.svg
// With weighter MSE (RGB weights: 0.3, 0.59, 0.11)
float3 bump3y (float3 x, float3 yoffset)
{
	float3 y = float3(1.,1.,1.) - x * x;
	y = saturate(y-yoffset);
	return y;
}
float3 spectral_zucconi (float x)
{
    // // w: [400, 700]
	// // x: [0,   1]
	// float x = saturate((w - 400.0)/ 300.0);

	const float3 cs = float3(3.54541723, 2.86670055, 2.29421995);
	const float3 xs = float3(0.69548916, 0.49416934, 0.28269708);
	const float3 ys = float3(0.02320775, 0.15936245, 0.53520021);

	return bump3y (	cs * (x - xs), ys);
}

float remap(float value, float inMin, float inMax, float outMin, float outMax) {
    float factor = (value - inMin) / (inMax - inMin);
    float v = factor * (outMax - outMin) + outMin;
    return v;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth/TargetHeight;

    float2 p = psInput.texCoord;
    //p.x -= 0.5;
    p -= 0.5;
    p.x *= aspectRatio * Aspect;


    float d = 0.5;// length(p);

    float2 dir1 = Target-Center;
    float ldir1 = length(dir1);

    float a1 = atan2(dir1.x, dir1.y);

    float2 dir2 = p-Center;
    float ldir2 = length(dir2);
    float a2 = atan2(dir2.x, dir2.y);
        
    float a3 = a1-a2;
    
    if(a3 > 3.1415 ) {
        a3 -= 2*3.1415;
    }
    else if(a3 <= -3.1415) {
        a3+= 2 * 3.1415;
    }

    float4 noise =  ImageA.SampleLevel(texSampler, float2(Time * 0.2 + a3 * 0.2, a3 * 5 + Time), 0.0) * 0.6
                    + ImageA.SampleLevel(texSampler, float2(Time * 0.2 /4, (a3 * 5 + Time) / 4), 0.0) * 0.4;

    a3 += noise.r * 0.5 * Noise;

    // Adjust curvature
    a3 *= Shape;
    float s = pow( sqrt(1-a3*a3), Shape2);

    // if(s<0.001)
    //     s= 1;

    float f = ldir2 / s / ldir1;

    float adjustedWidth = Width+ abs( pow(a3,2))*0.1 +  (noise.b + noise.g-1) * 0.6 * Noise * s;

    //d = max(d, f % 0.1 * 10);
    float hoop = smoothstep(1-adjustedWidth, 1-adjustedWidth  + 0.2,f) * smoothstep(1+adjustedWidth, 0.9+adjustedWidth,f);

    float4 color = float4(spectral_zucconi( remap(f, 1-adjustedWidth, 1 + adjustedWidth, 1,0) ),1);

    d = max(d, hoop);
    float repeats = 1/Complexity;
    //float segments = smoothstep(0.1,1, abs( (a3+3.1415) % repeats - repeats/2) * repeats*200 );
    float segments = abs( (a3+3.1415) % repeats - repeats/2) * Complexity *2;
    float filled = smoothstep(SegmentFill, SegmentFill + 0.2, segments);
    //return float4(filled,0,0,1);

    d = min(d, filled * hoop);
    //d = min(d, smoothstep(0,0.034, f % 0.1 % 10  ));


    float sSave = isnan(s) ? 0.91 : s;

    // d = min(d, smoothstep(0,0.034, f % 0.1 % 10  ));
    // d = min(d, smoothstep(0,0.1, sSave%0.1*10))*0.5;

    d = max(d, smoothstep(0.01, 0.006, length(p - Center)));
    d = max(d, smoothstep(0.01, 0.006, length(p - Target)));


    float r = ldir2 / 2 * 0.7;
    float2 mid = (Center+Target)/2;
    d = max(d, smoothstep(r, r-0.006, length(p - mid)) * 0.2 );

    return float4(color.rgb, d) * FillColor;

    // return float4(
    //     d.x, 
    //     0,
    //     0,
    //     //abs(a2)  % 0.1 * 10,
    // 1);
}