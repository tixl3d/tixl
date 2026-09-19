
Texture2D<float4> ImagePixels : register (t0);
RWTexture3D<float> LookUp : register (u0);

sampler texSampler : register(s0);

static const float3x3 fwdA = {1.0, 1.0, 1.0,
                       0.3963377774, -0.1055613458, -0.0894841775,
                       0.2158037573, -0.0638541728, -1.2914855480};
                       
static const float3x3 fwdB= {4.0767245293, -1.2681437731, -0.0041119885,
                       -3.3072168827, 2.6093323231, -0.7034763098,
                       0.2307590544, -0.3411344290,  1.7068625689};

static const float3x3 invB = {0.4121656120, 0.2118591070, 0.0883097947,
                       0.5362752080, 0.6807189584, 0.2818474174,
                       0.0514575653, 0.1074065790, 0.6302613616};
                       
static const float3x3 invA = {0.2104542553, 1.9779984951, 0.0259040371,
                       0.7936177850, -2.4285922050, 0.7827717662,
                       -0.0040720468, 0.4505937099, -0.8086757660};


inline float3 RgbToLCh(float3 col) {
    col = mul(col, invB);
    col= mul((sign(col) * pow(abs(col), 0.3333333333333)), invA);    

    float3 polar = 0;
    polar.x = col.x;
    polar.y = sqrt(col.y * col.y + col.z * col.z);
    polar.z = atan2(col.z, col.y);
    polar.z= polar.z / (2 * 3.141592) + 0.5;
    return polar;
}


inline float3 LChToRgb(float3 polar) {
    float3 col = 0; 
    col.x = polar.x;
    col.y = polar.y * cos(polar.z);
    col.z = polar.y * sin(polar.z);

    float3 lms = mul(col, fwdA);
    return mul( (lms * lms * lms), fwdB);   
}

[numthreads(1,1,1)]
void BuildLookupTable(uint3 threadID : SV_DispatchThreadID)
{
    uint width, height;
    ImagePixels.GetDimensions(width, height);

    float3 lchColor = (float3)threadID/255;

    int bestIndex= -1;
    float bestDistance = 99999999;

    for(int imageIndex=0; imageIndex< width && imageIndex< 1000; imageIndex++) 
    {
        float4 pixelColor = ImagePixels.SampleLevel(texSampler, float2((imageIndex + 0.5)/width, 0.5),0);
        float3 imageLch = RgbToLCh(pixelColor.rgb);

        float distance = length(imageLch - lchColor);
        if(distance < bestDistance) {
            bestDistance = distance;
            bestIndex = imageIndex;
        }
    }

    LookUp[threadID] = (float)bestIndex;
}
