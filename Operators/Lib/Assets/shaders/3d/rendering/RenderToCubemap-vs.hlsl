float4x4 objectToWorldMatrix;
float4x4 worldToCameraMatrix;
float4x4 cameraToObjectMatrix; // modelview inverse
float4x4 projMatrix;
float4x4 textureMatrix;

TextureCube CubeMap : register(t0);;
//Texture2D txDiffuse;

//Texture2D Image : register(t0);
sampler texSampler : register(s0);

float g_CubeSize = 256;
float g_CubeLod = 0;
float g_CubeLodCount = 1;

// float Roughness;
// int BaseMip;
// int NumSamples;

SamplerState samLinear
{
    Filter = MIN_MAG_MIP_LINEAR;
    AddressU = Wrap;
    AddressV = Wrap;
};

cbuffer Params : register(b0)
{
    float Roughness;
    int BaseMip;
    int NumSamples;
    float Exposure;
}


float mod(float a, float b)
{
    return a - b*floor(a/b);
}

float3 mod(float3 a, float b)
{
    return a - b*floor(a/b);
}

static const float PI = 3.14159265358979;

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

/*
float3 hemisphereSample_uniform(float u, float v) 
{
    float phi = v * 2.0 * PI;
    float cosTheta = 1.0 - u;
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
    return float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
}
    
    
float3 hemisphereSample_cos(float u, float v) 
{
    float phi = v * 2.0 * PI;
    float cosTheta = sqrt(1.0 - u);
    float sinTheta = sqrt(1.0 - cosTheta * cosTheta);
    return float3(cos(phi) * sinTheta, sin(phi) * sinTheta, cosTheta);
}


float3 importanceSampleGGX(float2 xi, float3 N)
{
    float alpha = Roughness * Roughness;
    float alpha2 = alpha*alpha;
    float phi = 2 * PI * xi.x;
    float cosTheta = sqrt((1 - xi.y) / (1 + (alpha2 - 1) * xi.y));
    float sinTheta = sqrt(1 - cosTheta*cosTheta);
    
    float3 H;
    H.x = sinTheta * cos(phi);
    H.y = sinTheta * sin(phi);
    H.z = cosTheta;
    
    float3 up = abs(N.z) < 0.999 ? float3(0,0,1) : float3(1,0,0);
    float3 tangentX = normalize(cross(up, N));
    float3 tangentY = cross(N, tangentX);
    
    return tangentX*H.x + tangentY*H.y + N*H.z;
}
*/





struct vsOutput
{
    float4 pos : SV_POSITION;
    float2 uv : TEXCOORD0;
};
 
void vsMain(out vsOutput o, uint id : SV_VERTEXID)
{
    o.uv = float2((id << 1) & 2, id & 2);
    o.pos = float4(o.uv * float2(2,-2) + float2(-1,1), 0, 1);
//o.uv = (o.pos.xy * float2(0.5,-0.5) + 0.5) * 4;
//o.uv.y = 1 - o.uv.y;
}
 
struct gsOutput
{
    float4 pos : SV_POSITION;
    float3 nrm : TEXCOORD0;
    float4 col : COLOR0;
    uint face : SV_RENDERTARGETARRAYINDEX;
};

float4 colorOfBox(uint face)
{
    float4 c = float4(0,0,0,1);

    if (face == 0) // posx (red)
    {
        c = float4(1,0,0,1);
    }
    else if (face == 1) // negx (cyan)
    {
        c = float4(1,1,0,1);
    }
    else if (face == 2) // posy (green)
    {
        c = float4(0,1,0,1);
    }
    else if (face == 3) // negy (magenta)
    {
        c = float4(0,1,1,1);
    }
    else if (face == 4) // posz (blue)
    {
        c = float4(0,0,1,1);
    }
    else // if (i.face == 5) // negz (yellow)
    {
        c = float4(1,0,1,1);
    }
 
    return c;
}

float3 UvAndIndexToBoxCoord(float2 uv, uint face)
{
    float3 n = float3(0,0,0);
    float3 t = float3(0,0,0);

    if (face == 0) // posx (red)
    {
        n = float3(1,0,0);
        t = float3(0,1,0);
    }
    else if (face == 1) // negx (cyan)
    {
        n = float3(-1,0,0);
        t = float3(0,1,0);
    }
    else if (face == 2) // posy (green)
    {
        n = float3(0,-1,0);
        t = float3(0,0,-1);
    }
    else if (face == 3) // negy (magenta)
    {
        n = float3(0,1,0);
        t = float3(0,0,1);
    }
    else if (face == 4) // posz (blue)
    {
        n = float3(0,0,-1);
        t = float3(0,1,0);
    }
    else // if (i.face == 5) // negz (yellow)
    {
        n = float3(0,0,1);
        t = float3(0,1,0);
    }
 
    float3 x = cross(n, t);
 
    uv = uv * 2 - 1;
     
    n = n + t*uv.y + x*uv.x;
    n.y *= -1;
    n.z *= -1;
    return n;
}
 
[maxvertexcount(18)]
void gsMain(triangle vsOutput input[3], inout TriangleStream<gsOutput> output)
{
    for( int f = 0; f < 6; ++f )
    {
        for( int v = 0; v < 3; ++v )
        {
            gsOutput o;
            o.pos = input[v].pos;
            o.nrm = UvAndIndexToBoxCoord(input[v].uv, f);
            o.col = colorOfBox(f);
            o.face = f;
            output.Append(o);
        }
        output.RestartStrip();
    }
}
 
// static const SamplerState g_samCube
// {
//     Filter = MIN_MAG_MIP_LINEAR;
//     AddressU = Clamp;
//     AddressV = Clamp;
// };

/* 
cbuffer mip : register(b0)
{
    float g_CubeSize;
    float g_CubeLod;
    float g_CubeLodCount;
};
*/ 
 
// http://holger.dammertz.org/stuff/notes_HammersleyOnHemisphere.html
/*
float radicalInverse_VdC(uint bits) {
     bits = (bits << 16u) | (bits >> 16u);
     bits = ((bits & 0x55555555u) << 1u) | ((bits & 0xAAAAAAAAu) >> 1u);
     bits = ((bits & 0x33333333u) << 2u) | ((bits & 0xCCCCCCCCu) >> 2u);
     bits = ((bits & 0x0F0F0F0Fu) << 4u) | ((bits & 0xF0F0F0F0u) >> 4u);
     bits = ((bits & 0x00FF00FFu) << 8u) | ((bits & 0xFF00FF00u) >> 8u);
     return float(bits) * 2.3283064365386963e-10; // / 0x100000000
 }
 */
 // http://holger.dammertz.org/stuff/notes_HammersleyOnHemisphere.html
float2 Hammersley(uint i, uint N)
{
    return float2(float(i)/float(N), radicalInverse_VdC(i));
}

float G_schlick_IBL(float NoV, float NoL, float roughness)
{
    float k = roughness*roughness/2.0f;
    float one_minus_k = 1.0f - k;
    return (NoL / (NoL * one_minus_k + k)) * (NoV / (NoV * one_minus_k + k) );
}
 
// Image-Based Lighting
// http://www.unrealengine.com/files/downloads/2013SiggraphPresentationsNotes.pdf
float3 ImportanceSampleGGX( float2 Xi, float roughness, float3 N )
{
    float a = roughness * roughness;
    float Phi = 2 * PI * Xi.x;
    float CosTheta = sqrt( (1 - Xi.y) / ( 1 + (a*a - 1) * Xi.y ) );
    float SinTheta = sqrt( 1 - CosTheta * CosTheta );
    float3 H;
    H.x = SinTheta * cos( Phi );
    H.y = SinTheta * sin( Phi );
    H.z = CosTheta;
    float3 UpVector = abs(N.z) < 0.999 ? float3(0,0,1) : float3(1,0,0);
    float3 TangentX = normalize( cross( UpVector, N ) );
    float3 TangentY = cross( N, TangentX );
    // Tangent to world space
    return TangentX * H.x + TangentY * H.y + N * H.z;
}
 
 
// Ignacio Castano via http://the-witness.net/news/2012/02/seamless-cube-map-filtering/
float3 fix_cube_lookup_for_lod(float3 v, float cube_size, float lod)
{
    float M = max(max(abs(v.x), abs(v.y)), abs(v.z));
    float scale = 1 - exp2(lod) / cube_size;
    if (abs(v.x) != M) v.x *= scale;
    if (abs(v.y) != M) v.y *= scale;
    if (abs(v.z) != M) v.z *= scale;
    return v;
}

float D_GGX(float NoH, float roughness)
{
    // towbridge-reitz / GGX distribution
    float alpha = roughness*roughness;
    float alpha2 = alpha*alpha;
    float NoH2 = NoH*NoH;
    float f = NoH2 * (alpha2 - 1.0) + 1;
    return alpha2 / (3.1415 * f*f);
}


//#define REFERENCE_ON

float4 psMain(in gsOutput i) : SV_TARGET0
{
    float3 N = normalize(i.nrm);
//    return colorOfBox(i.face);
     
    float4 totalRadiance = float4(0,0,0,0);
    float roughness = max(Roughness, 0.01);
    
#ifdef REFERENCE_ON
    uint NUM_SAMPLES = 25000;
#else
    uint NUM_SAMPLES = NumSamples;
#endif

    [fastopt]
    for (uint j = 0; j < NUM_SAMPLES; ++j)
    {
        float2 Xi = Hammersley(j, NUM_SAMPLES);
        float3 H = ImportanceSampleGGX(Xi, roughness, N);
        float3 L = 2*dot(N, H)*H - N;
        float NdotL = saturate(dot(N, L));

        if (NdotL > 0)
        {

#ifdef REFERENCE_ON
            float mipmapLevel = 0;
#else
            float NdotH = saturate(dot(N,H));        
            
            float pdf_H = D_GGX(NdotH, roughness)*NdotH;
            float pdf = pdf_H/(4*NdotH); // transform from half to incoming
            
            float area = 2*3.1415f; // hemispehere area
            float solidangleSample = area/(NUM_SAMPLES*pdf); // solid angle for sample
            float solidangleTexel = area/(3.0*128*128); // solid angle per cubemap texel 

            float mipmapLevel = clamp(0.5 * log2(solidangleSample/solidangleTexel), BaseMip, 9);
#endif

            totalRadiance.rgb += CubeMap.SampleLevel(texSampler, L, mipmapLevel).rgb*NdotL;
            totalRadiance.w += NdotL;
        }
    }
//return float4(Roughness, Roughness, Roughness, 1);
    return float4(totalRadiance.rgb / totalRadiance.w * Exposure, 1);
}






// technique10 Render
// {
//     pass P0
//     {
//         SetVertexShader( CompileShader( vs_5_0, vsMain() ) );
//         SetGeometryShader( CompileShader( gs_5_0, gsMain() ) );
//         SetPixelShader( CompileShader( ps_5_0, psMain() ) );
//     }
// } 