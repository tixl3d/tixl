cbuffer ParamConstants : register(b0)
{
    float InnerZoneStart;
    float InnerZoneEnd;

    float EdgeZoneStart;
    float EdgeZoneEnd;

    float MatteBoxStart;
    float MatteBoxEnd;

    float Time;
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



float mod(float x, float y) {
    return (x - y * floor(x / y));
} 



float remap(float value, float inMin, float inMax, float outMin, float outMax) {
    float factor = (value - inMin) / (inMax - inMin);
    float v = factor * (outMax - outMin) + outMin;
    return v;
}

float getZones(float2 p) {
    float dToRight = 1-p.x;
    float dToLeft = p.x;
    float dToUp = p.y;
    float dToBottom = 1-p.y;
    
    float d = min(dToLeft, dToRight);
    d = min(d, dToUp);
    d = min(d, dToBottom);
    d*=2;

    float dToCenter = length(p-0.5) * 2;
    float cInnerZone = smoothstep(InnerZoneEnd, InnerZoneStart, dToCenter);
    float cEdgeZone = smoothstep(EdgeZoneStart, EdgeZoneEnd, 1-d);

    float cMatteBox = smoothstep(MatteBoxEnd, MatteBoxStart, 1-d);
    return (cInnerZone + cEdgeZone) * cMatteBox;
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    float aspectRatio = TargetWidth/TargetHeight;

    float2 p = psInput.texCoord;
    float2 pWithApsect = float2(p.x  * TargetWidth, p.y * TargetHeight);

    bool showInner = (pWithApsect.x + pWithApsect.y + Time * 60) % 10 < 2;
    bool showOuter = (pWithApsect.x + pWithApsect.y + Time * 60+ 5) % 10 < 2;

    float c = showInner ?  getZones(p) : 0;

    float ff = abs(p.x-0.5)*2;

    float pya = abs(p.y*2 - 1);
    float c2 = 0;
    if(showOuter && (pya +0.5)  < ff) {
        float edgeD = ff*2-1;
        //return float4(1/edgeD, 0,0,1);
        float sign = p.x < 0.5 ? -1 : 1;

        //float xx = p.x < 0.5 ? -pow(p.x,2) : -pow(1-p.x,2);
        float2 p2 = float2( (1/ edgeD),   pya+0.1);

        c2 = getZones(p2);
        //return float4(c2,0,0,1);
    }


    return float4(c,c2,c2,1);

    // float d = 0.5;// length(p);

    // float2 dir1 = Target-Center;
    // float ldir1 = length(dir1);

    // float a1 = atan2(dir1.x, dir1.y);

    // float2 dir2 = p-Center;
    // float ldir2 = length(dir2);
    // float a2 = atan2(dir2.x, dir2.y);
        
    // float a3 = a1-a2;
    
    // if(a3 > 3.1415 ) {
    //     a3 -= 2*3.1415;
    // }
    // else if(a3 <= -3.1415) {
    //     a3+= 2 * 3.1415;
    // }

    // float4 noise =  ImageA.SampleLevel(texSampler, float2(Time * 0.2 + a3 * 0.2, a3 * 5 + Time), 0.0) * 0.6
    //                 + ImageA.SampleLevel(texSampler, float2(Time * 0.2 /4, (a3 * 5 + Time) / 4), 0.0) * 0.4;

    // a3 += noise.r * 0.5 * Noise;

    // // Adjust curvature
    // a3 *= Shape;
    // float s = pow( sqrt(1-a3*a3), Shape2);

    // // if(s<0.001)
    // //     s= 1;

    // float f = ldir2 / s / ldir1;

    // float adjustedWidth = Width+ abs( pow(a3,2))*0.1 +  (noise.b + noise.g-1) * 0.6 * Noise * s;

    // //d = max(d, f % 0.1 * 10);
    // float hoop = smoothstep(1-adjustedWidth, 1-adjustedWidth  + 0.2,f) * smoothstep(1+adjustedWidth, 0.9+adjustedWidth,f);

    // float4 color = float4(spectral_zucconi( remap(f, 1-adjustedWidth, 1 + adjustedWidth, 1,0) ),1);

    // d = max(d, hoop);
    // float repeats = 1/Complexity;
    // //float segments = smoothstep(0.1,1, abs( (a3+3.1415) % repeats - repeats/2) * repeats*200 );
    // float segments = abs( (a3+3.1415) % repeats - repeats/2) * Complexity *2;
    // float filled = smoothstep(SegmentFill, SegmentFill + 0.2, segments);
    // //return float4(filled,0,0,1);

    // d = min(d, filled * hoop);
    // //d = min(d, smoothstep(0,0.034, f % 0.1 % 10  ));


    // float sSave = isnan(s) ? 0.91 : s;

    // // d = min(d, smoothstep(0,0.034, f % 0.1 % 10  ));
    // // d = min(d, smoothstep(0,0.1, sSave%0.1*10))*0.5;

    // d = max(d, smoothstep(0.01, 0.006, length(p - Center)));
    // d = max(d, smoothstep(0.01, 0.006, length(p - Target)));


    // float r = ldir2 / 2 * 0.7;
    // float2 mid = (Center+Target)/2;
    // d = max(d, smoothstep(r, r-0.006, length(p - mid)) * 0.2 );

    // return float4(color.rgb, d) * FillColor;

    // // return float4(
    // //     d.x, 
    // //     0,
    // //     0,
    // //     //abs(a2)  % 0.1 * 10,
    // // 1);
}