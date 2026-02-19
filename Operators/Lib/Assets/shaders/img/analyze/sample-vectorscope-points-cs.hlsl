#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float EnlargeCenter;
}

RWStructuredBuffer<LegacyPoint> ResultPoints : u0;
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

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
        q.x*0.5);  
}

float3 rgb2hsl( in float3 c ){
  float h = 0.0;
	float s = 0.0;
	float l = 0.0;
	float r = c.r;
	float g = c.g;
	float b = c.b;
	float cMin = min( r, min( g, b ) );
	float cMax = max( r, max( g, b ) );

	l = ( cMax + cMin ) / 2.0;
	if ( cMax > cMin ) {
		float cDelta = cMax - cMin;
        
        //s = l < .05 ? cDelta / ( cMax + cMin ) : cDelta / ( 2.0 - ( cMax + cMin ) ); Original
		s = l < .0 ? cDelta / ( cMax + cMin ) : cDelta / ( 2.0 - ( cMax + cMin ) );
        
		if ( r == cMax ) {
			h = ( g - b ) / cDelta;
		} else if ( g == cMax ) {
			h = 2.0 + ( b - r ) / cDelta;
		} else {
			h = 4.0 + ( r - g ) / cDelta;
		}

		if ( h < 0.0) {
			h += 6.0;
		}
		h = h / 6.0;
	}
	return float3( h, s, l );
}

float3 rgb2yuv(float3 rgb) 
{
    return float3(    
	rgb.r * 0.299 + rgb.g * 0.587 + rgb.b * 0.114,
	rgb.r * -0.169 + rgb.g * -0.331 + rgb.b * 0.5 + 0.5,
	rgb.r * 0.5 + rgb.g * -0.419 + rgb.b * -0.081 + 0.5
    );

}

// Computes 
float GetZoomFactor(float u) 
{
    return pow(abs((u - 0.5) *2), EnlargeCenter);
}


[numthreads(256,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint pointCount, stride;
    ResultPoints.GetDimensions(pointCount,stride);

    float root = sqrt(pointCount);
    float row = i.x / root;
    float column = i.x % root;

    float2 uv = float2(column, row) / root;

    float4 c = inputTexture.SampleLevel(texSampler, uv, 0.0);

    float3 YUV = rgb2yuv(saturate(c.rgb));
    float radius = 0.35*1.4*2.1; 
    

    float zoom = pow(length((YUV.yz - 0.5) *2), EnlargeCenter);
    ResultPoints[i.x].Position = float3(
        (YUV.y-0.5) * radius * zoom,  
        (YUV.z-0.5) * radius * zoom,
        0 );


    // float3 hsb = rgb2hsb(c.rgb);
    // float hueAngle = (hsb.x + 0.035) * 2*PI;


    // //float hueAngleY = (hsb.x + 0.06) * 2*PI;

    // ResultPoints[i.x].position = float3(
    //     -sin(hueAngle) * hsb.y * radius, 
    //     cos(hueAngle) * hsb.y * radius,
    //     0 );

    ResultPoints[i.x].Rotation = c;
    ResultPoints[i.x].W = 1;
}