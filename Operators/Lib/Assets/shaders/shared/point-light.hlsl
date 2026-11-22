#ifndef __POINT_LIGHT_HLSL
#define __POINT_LIGHT_HLSL

// Mirrors T3.Core.Rendering.Light. Despite the name it holds every light type; shaders that only read
// position, intensity, color, range and decay treat all lights as point lights.
#define LIGHT_TYPE_POINT 0
#define LIGHT_TYPE_SPOT 1
#define LIGHT_TYPE_DIRECTIONAL 2

struct PointLight
{
    float3 position;
    float intensity;
    float4 color;
    float range;
    float decay;
    uint type;
    float spotCosInner;
    float3 direction; // the light travels along it (spot, directional)
    float spotCosOuter;
    float3 right;
    float __padding0;
    float2 size;
    float2 __padding1;
};

// L points from the surface toward the light. radiance includes falloff and the spot cone.
void EvaluateLight(PointLight light, float3 worldPosition, out float3 L, out float3 radiance)
{
    if (light.type == LIGHT_TYPE_DIRECTIONAL)
    {
        L = -light.direction;
        radiance = light.color.rgb * light.intensity;
        return;
    }

    float3 toLight = light.position - worldPosition;
    float distance = length(toLight);
    L = toLight / max(distance, 1e-4);
    float intensity = light.intensity / (pow(distance / light.range, light.decay) + 1);

    if (light.type == LIGHT_TYPE_SPOT)
    {
        float cosAngle = dot(-L, light.direction);
        intensity *= smoothstep(light.spotCosOuter, max(light.spotCosInner, light.spotCosOuter + 1e-4), cosAngle);
    }

    radiance = light.color.rgb * intensity;
}

#endif
