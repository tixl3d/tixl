cbuffer ParamConstants : register(b0)
{
    float MaxSteps;
    float StepSize;
    float MinDistance;
    float MaxDistance;

    float Minrad;
    float Scale;
    float2 Fold;

    float3 Clamping;
    float1 __align1__;
    float3 Increment;
    float1 __align2__;

    float4 Surface1;
    float4 Surface2;
    float4 Surface3;
    float4 Specular;
    float4 Glow;
    float4 AmbientOcclusion;
    float4 Background;

    float2 Spec;
    float AODistance;
    float Fog;

    float3 LightPos;
    float DistToColor;
}

cbuffer Transforms : register(b1)
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

//>>> _common parameters
float4x4 objectToWorldMatrix;
float4x4 worldToCameraMatrix;
float4x4 projMatrix;
Texture2D txDiffuse;
float2 RenderTargetSize;
//<<< _common parameters

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
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

Texture2D<float4> ImageA : register(t0);
sampler texSampler : register(s0);

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
    return output;

    return output;
}

#define mod ((x), (y))((x) - (y) * floor((x) / (y)))

float sdBox(in float2 p, in float2 b)
{
    float2 d = abs(p) - b;
    return length(
               max(d, float2(0, 0))) +
           min(max(d.x, d.y),
               0.0);
}

struct VS_IN
{
    float4 pos : POSITION;
    float2 texCoord : TEXCOORD;
};

struct PS_IN
{
    float4 pos : SV_POSITION;
    float2 texCoord : TEXCOORD0;
    float3 worldTViewPos : TEXCOORD1;
    float3 worldTViewDir : TEXCOORD2;
};

static float BOX_RADIUS = 0.005;
float dBox(float3 p, float3 b)
{
    return length(max(abs(p) - b + float3(BOX_RADIUS, BOX_RADIUS, BOX_RADIUS), 0.0)) - BOX_RADIUS;
}

static int mandelBoxIterations = 7;

float dMandelbox(float3 pos)
{
    float4 pN = float4(pos, 1);
    // return dStillLogo(pN);

    // precomputed constants
    float minRad2 = clamp(Minrad, 1.0e-9, 1.0);
    float4 scale = float4(Scale, Scale, Scale, abs(Scale)) / minRad2;
    float absScalem1 = abs(Scale - 1.0);
    float AbsScaleRaisedTo1mIters = pow(abs(Scale), float(1 - mandelBoxIterations));
    float DIST_MULTIPLIER = StepSize;

    float4 p = float4(pos, 1);
    float4 p0 = p; // p.w is the distance estimate

    for (int i = 0; i < mandelBoxIterations; i++)
    {
        // box folding:
        p.xyz = abs(1 + p.xyz) - p.xyz - abs(1.0 - p.xyz);                 // add;add;abs.add;abs.add (130.4%)
        p.xyz = clamp(p.xyz, Clamping.x, Clamping.y) * Clamping.z - p.xyz; // min;max;mad

        // sphere folding: if (r2 < minRad2) p /= minRad2; else if (r2 < 1.0) p /= r2;
        float r2 = dot(p.xyz, p.xyz);
        p *= clamp(max(minRad2 / r2, minRad2), Fold.x, Fold.y); // dp3,div,max.sat,mul
        p.xyz += float3(Increment.x, Increment.y, Increment.z);
        // scale, translate
        p = p * scale + p0;
    }
    float d = ((length(p.xyz) - absScalem1) / p.w - AbsScaleRaisedTo1mIters) * DIST_MULTIPLIER;
    return d;
}

//---------------------------------------
float GetDistance(float3 p)
{
    float d = 0;
    //>>>>
    d = dMandelbox(p);
    //<<<<

    return d;

    // d= max(dBox( p + float3(SpherePos.x - SpherePos.y , 0,0), float3(SpherePos.y,3,3)), dLogo );
    // return max(d, dBox(p + float3(0, 0, 0), float3(2, 0.5, 2)));
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
    float dt = .0001;
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

// Compute the color at |pos|.
float3 ComputeDiffuseColor(float3 pos)
{
    float3 p = pos, p0 = p;
    float trap = 1.0;

    for (int i = 0; i < 3; i++)
    {
        p.xyz = clamp(p.xyz, -1.0, 1.0) * 2.0 - p.xyz;
        float r2 = dot(p.xyz, p.xyz);
        p *= clamp(max(Minrad / r2, Minrad), 0.0, 1.0);
        p = p * Scale + p0.xyz;
        trap = min(trap, r2);
    }
    // |c.x|: log final distance (fractional iteration count)
    // |c.y|: spherical orbit trap at (0,0,0)
    float2 c = clamp(float2(0.33 * log(dot(p, p)) - 1.0, sqrt(trap)), 0.0, 1.0);

    return lerp(lerp(Surface1.xyz, Surface2.xyz, c.y), Surface3.xyz, c.x);
}

float4 psMain(vsOutput input) : SV_TARGET
{
    float3 p = input.worldTViewPos;
    float3 tmpP = p;
    float3 dp = normalize(input.worldTViewDir);

    float totalD = 0.0;
    float D = 3.4e38;
    D = StepSize;
    float extraD = 0.0;
    float lastD;
    int steps;
    int maxSteps = (int)(MaxSteps - 0.5);

    // Simple iterator
    for (steps = 0; steps < maxSteps && abs(D) > MinDistance; steps++)
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

        col = ComputeDiffuseColor(p);
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
        a = 0.5;
    }

    // Glow is based on the number of steps.
    col = lerp(col, Glow.rgb, float(steps) / float(MaxSteps) * Glow.a);
    float f = clamp(log(length(p - input.worldTViewPos) / Fog), 0, 1);
    col = lerp(col, Background.rgb, f);
    a *= (1 - f * Background.a);
    return float4(col, a);
}
