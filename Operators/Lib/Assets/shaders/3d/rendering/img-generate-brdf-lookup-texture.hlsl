
RWTexture2D<float2> Output;

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

float radicalInverse_VdC(uint bits) 
{
     bits = (bits << 16u) | (bits >> 16u);
     bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
     bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
     bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
     bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
     return float(bits) * 2.3283064365386963e-10; // / 0x100000000
}


float2 hammersley2d(uint i, uint N) 
{
    return float2(float(i)/float(N), radicalInverse_VdC(i));
}
    

float G_schlick_IBL(float NoV, float NoL, float roughness)
{
    float k = roughness*roughness/2.0f;
    float one_minus_k = 1.0f - k;
    return (NoL / (NoL * one_minus_k + k)) * (NoV / (NoV * one_minus_k + k) );
}

float GeometrySchlickGGX (float NdotV, float roughness)
{
    float a = roughness;
    float k = (a * a) / 2.0f;

    float nom   = NdotV;
    float denom = NdotV * (1.0f - k) + k;

    return nom / denom;
}

float GeometrySmith(float3 N, float3 V, float3 L, float roughness)
{
    float NdotV = saturate(dot(N, V));
    float NdotL = saturate(dot(N, L));
    float ggx2 = GeometrySchlickGGX(NdotV, roughness);
    float ggx1 = GeometrySchlickGGX(NdotL, roughness);

    return ggx1 * ggx2;
}


float3 importanceSampleGGX(float2 xi, float roughness, float3 N)
{
    const float TWO_PI = 6.283185307179f;
    float alpha = roughness * roughness;
    float alphaSquared = alpha*alpha;
    float phi = TWO_PI * xi.x;
    float cosTheta = sqrt((1.0f - xi.y) / (1.0f + (alphaSquared - 1.0f) * xi.y));
    float sinTheta = sqrt(1.0f - cosTheta*cosTheta);
    
    float3 H;
    H.x = sinTheta * cos(phi);
    H.y = sinTheta * sin(phi);
    H.z = cosTheta;
    
    float3 up = abs(N.z) < 0.999 ? float3(0,0,1) : float3(1,0,0);
    float3 tangentX = normalize(cross(up, N));
    float3 tangentY = cross(N, tangentX);
    
    return tangentX*H.x + tangentY*H.y + N*H.z;
}

// taken from Brian Karis' siggraph2013 paper
float2 IntegrateBRDF(float NoV, float roughness)
{ 
    float3 V; 
    V.x = sqrt(1.0f - NoV*NoV); // sin 
    V.y = 0; 
    V.z = NoV; // cos

    const float3 N = float3(0, 0, 1);
    float A = 0; 
    float B = 0;

    const uint NUM_SAMPLES = 100;
        
    for (uint i = 0; i < NUM_SAMPLES; i++)
    { 
        float2 Xi = hammersley2d(i, NUM_SAMPLES);
        float3 H = importanceSampleGGX(Xi, roughness, N);
        float3 L = 2*dot(V, H)*H - V;
        
        float NoL = saturate(L.z);
        float NoH = saturate(H.z);
        float VoH = saturate(dot(V, H));
        
        if (NoL > 0.0f)
        {
            float G = G_schlick_IBL(NoV, NoL, roughness);
            float G_Vis = G*VoH/(NoH*NoV);
            float Fc = pow(1.0f - VoH, 5.0f);
            A += (1.0f - Fc)*G_Vis;
            B += Fc * G_Vis;
        }
    }
    
    return float2(A, B) / float(NUM_SAMPLES);
}

[numthreads(16, 16, 1)]
void main(uint3 threadID : SV_DispatchThreadID)
{
    uint width, height;
    Output.GetDimensions(width, height);
    float roughness = float(threadID.y)/(width - 1.0f);
    float NoV = float(threadID.x)/(height - 1.0f);
    float2 scaleBias = IntegrateBRDF(NoV, roughness);
    Output[threadID.xy] = scaleBias;
}

// float4 psMain(vsOutput psInput) : SV_TARGET
// {    
//     // float2 p = psInput.texCoord / Size;
//     // float2 a = mod(p,1);
//     // float t= (a.x > 0.5 && a.y < 0.5) ||  (a.x < 0.5 && a.y > 0.5) ? 0 :1;
//     // return lerp(ColorA, ColorB,  t);
//     float2 uv = psInput.texCoord;

//     float roughness = uv.x;// float(threadID.y)/(width - 1.0f);
//     float NoV =  NoV = uv.y;// float(threadID.x)/(height - 1.0f);
//     float2 scaleBias = IntegrateBRDF(NoV, roughness);
//     return float4(1,1,0,1);
//     return scaleBias;
// }
