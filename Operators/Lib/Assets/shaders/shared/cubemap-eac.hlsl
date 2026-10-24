#ifndef __CUBEMAP_EAC
#define __CUBEMAP_EAC

// Equi-angular cube maps space texels evenly by angle instead of evenly across the face plane.
// Face coordinates are in [-1, 1].

float2 EacToCubeFace(float2 eac)
{
    return tan(eac * 0.78539816);
}

float2 CubeFaceToEac(float2 face)
{
    return atan(face) * 1.27323954;
}

// Moves a lookup direction so that a regular cube map sample reads the texel of an equi-angular cube map.
// The warp is odd and applied per axis, so it does not depend on the orientation of the faces.
float3 EncodeEacDirection(float3 direction)
{
    float3 a = abs(direction);
    if (a.x >= a.y && a.x >= a.z)
        return float3(sign(direction.x), CubeFaceToEac(direction.yz / a.x));

    if (a.y >= a.z)
    {
        float2 eac = CubeFaceToEac(direction.xz / a.y);
        return float3(eac.x, sign(direction.y), eac.y);
    }

    return float3(CubeFaceToEac(direction.xy / a.z), sign(direction.z));
}

#endif
