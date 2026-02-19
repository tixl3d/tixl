cbuffer Params : register(b0)
{
    /*{FLOAT_PARAMS}*/
}

cbuffer ParamConstants : register(b1)
{
    float MaxSteps;
    float StepSize;
    float MinDistance;
    float MaxDistance;

    float Fog;
    float DistToColor;
    float AODistance;
    float __padding1;

    float4 Specular;
    float4 Glow;
    float4 AmbientOcclusion;
    float4 Background;

    float3 LightPos;
    float __padding;

    float2 Spec;
}

cbuffer Transforms : register(b2)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
    float3 viewDir : VPOS;
    float3 worldTViewDir : TEXCOORD1;
    float3 worldTViewPos : TEXCOORD2;
};

static const float3 Quad[] =
    {
        float3(-1, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(-1, 1, 0),
        float3(-1, -1, 0),
};

vsOutput vsMain4(uint vertexId : SV_VertexID)
{
    vsOutput output;
    float4 quadPos = float4(Quad[vertexId], 1);
    float2 texCoord = quadPos.xy * float2(0.5, -0.5) + 0.5;
    output.texCoord = texCoord;
    output.position = quadPos;
    float4x4 ViewToWorld = ClipSpaceToWorld; // CameraToWorld ;

    float4 viewTNearFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 0.0, 1.0);
    float4 worldTNearFragPos = mul(viewTNearFragPos, ViewToWorld);
    worldTNearFragPos /= worldTNearFragPos.w;

    float4 viewTFarFragPos = float4(texCoord.x * 2.0 - 1.0, -texCoord.y * 2.0 + 1.0, 1.0, 1.0);
    float4 worldTFarFragPos = mul(viewTFarFragPos, ViewToWorld);
    worldTFarFragPos /= worldTFarFragPos.w;

    output.worldTViewDir = normalize(worldTFarFragPos.xyz - worldTNearFragPos.xyz);
    output.worldTViewPos = worldTNearFragPos.xyz;

    output.viewDir = -normalize(float3(CameraToWorld._31, CameraToWorld._32, CameraToWorld._33));

    return output;
}

//--- Field functions -----------------------
/*{FIELD_FUNCTIONS}*/

//-------------------------------------------
float4 GetField(float4 p)
{
    float4 f = 1;
    /*{FIELD_CALL}*/
    return f;
}

float GetDistance(float3 p3)
{
    return GetField(float4(p3.xyz, 0)).w;
}
//---------------------------------------------------

// Blinn-Phong shading model with rim lighting (diffuse light bleeding to the other side).
// |normal|, |view| and |light| should be normalized.
float3 ComputedShadedColor(float3 normal, float3 view, float3 light, float3 diffuseColor)
{
    float3 halfLV = normalize(light + view);
    float clampedSpecPower = max(Spec.y, 0.001);
    float spe = pow(max(dot(normal, halfLV), Spec.x), clampedSpecPower);
    float dif = dot(normal, light) * 0.1 + 0.15;
    return dif * diffuseColor + spe * Specular.rgb;
}

float3 GetNormal(float3 p, float offset)
{
    float dt = .01;
    float3 n = float3(GetDistance(p + float3(dt, 0, 0)),
                      GetDistance(p + float3(0, dt, 0)),
                      GetDistance(p + float3(0, 0, dt))) -
               GetDistance(p);
    return normalize(n);
}

float ComputeAO(float3 aoposition, float3 aonormal, float aodistance, float aoiterations, float aofactor)
{
    float ao = 0.0;
    float k = aofactor;
    aodistance /= aoiterations;
    for (int i = 1; i < 4; i += 1)
    {
        ao += (i * aodistance - GetDistance(aoposition + aonormal * i * aodistance)) / pow(2, i);
    }
    return 1.0 - k * ao;
}

static float MAX_DIST = 300;

float DepthFromWorldSpace(float distFromCamera, float nearPlane, float farPlane)
{
    // Convert a world-space distance to a 0..1 depth.
    // Assumes a linear mapping from nearPlane..farPlane -> 0..1
    return saturate((distFromCamera - nearPlane) / (farPlane - nearPlane));
}

float DepthFromWorldSpace2(float dist, float near, float far)
{
    // Convert a world-space distance to a 0..1 depth.
    // Assumes a linear mapping from nearPlane..farPlane -> 0..1
    // return saturate((distFromCamera - nearPlane) / (farPlane - nearPlane));
    return far * (dist - near) / (dist * (far - near));
}

// float4 psMain(vsOutput input) : SV_TARGET

struct PSOutput
{
    float4 color : SV_Target;
    float depth : SV_Depth;
};

PSOutput psMain(vsOutput input)
{
    float3 eye = input.worldTViewPos;

    // Early test. This will lead to z-problems later
    // eye = mul(float4(eye,1), ObjectToWorld).xyz;
    float3 p = eye;
    float3 tmpP = p;
    float3 dp = normalize(input.worldTViewDir);
    // dp = mul(float4(dp,0), ObjectToWorld).xyz;

    float totalD = 0.0;
    float D = 3.4e38;
    D = StepSize;
    float extraD = 0.0;
    float lastD;
    int steps;
    int maxSteps = (int)(MaxSteps - 0.5);

    // Simple iterator
    for (steps = 0; steps < maxSteps && abs(D) > MinDistance && D < MaxDistance; steps++)
    {
        D = GetDistance(p);
        p += dp * D;
    }

    p += totalD * dp;

    // Color the surface with Blinn-Phong shading, ambient occlusion and glow.
    float3 col = Background.rgb;
    float a = 1;

    // We've got a hit or we're not sure.
    if (D < MAX_DIST)
    {
        float3 n = normalize(GetNormal(p, D));
        n = normalize(n);

        col = Specular.rgb;
        col = ComputedShadedColor(n, -dp, LightPos, col);

        col = lerp(AmbientOcclusion.rgb, col, ComputeAO(p, n, AODistance, 3, AmbientOcclusion.a));

        // We've gone through all steps, but we haven't hit anything.
        // Mix in the background color.
        if (D > MinDistance)
        {
            a = 1 - clamp(log(D / MinDistance) * DistToColor, 0.0, 1.0);
            col = lerp(col, Background.rgb, a);
        }
    }
    else
    {
        //        a = 0;
    }

    PSOutput result;

    // Glow is based on the number of steps.
    float glowEffect = float(steps) / MaxSteps;
    result.color = float4(glowEffect.rrr, 1);
    float depth = dot(eye - p, -input.viewDir);

    // result.depth = input.texCoord;
    result.depth = DepthFromWorldSpace2(depth, 0.01, 1000);
    return result;

    //

    col = lerp(col, Glow.rgb, float(steps) / float(MaxSteps) * Glow.a);

    float f = clamp(log(length(p - input.worldTViewPos) / Fog), 0, 1);

    // col = lerp(col, Background.rgb, f);

    // a *= (1 - f * Background.a);

    // if (a < 0.6)
    // {
    //     discard;
    // }

    result.color = float4(clamp(col, 0, 1000), saturate(a));
    // result.color = float4(1, 1, 0, 1);
    //   result.depth = totalD; // length(p);

    // float depth = dot(eye - p, -input.viewDir);

    // result.depth = input.texCoord;
    // result.depth = DepthFromWorldSpace2(depth, 0.01, 1000);
    // result.color = float4(depth.xxx, 1);
    //  result.depth = DepthFromWorldSpace2(length(eye - p), 0.01, 1000);
    return result;

    // return float4(a.xxx, 1);
    // return float4(col, a);
}
