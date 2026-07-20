cbuffer Params : register(b0)
{
    row_major float4x4 Homography;
    float4 SourceTLTR; // source UV corners: TL.xy, TR.xy
    float4 SourceBRBL; // source UV corners: BR.xy, BL.xy
    float4 Color;
}

Texture2D<float4> InputTexture : register(t0);
sampler texSampler : register(s0);

// Unit quad corners: TL, TR, BR / TL, BR, BL
static const float2 Corners[] =
    {
        float2(0, 0),
        float2(1, 0),
        float2(1, 1),
        float2(0, 0),
        float2(1, 1),
        float2(0, 1),
};

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

vsOutput vsMain(uint vertexId : SV_VertexID)
{
    vsOutput output;
    float2 corner = Corners[vertexId];

    // The homography acts on (x, y, w); the rasterizer's perspective divide completes it.
    output.position = mul(float4(corner, 0, 1), Homography);

    // Sample the surface's source region: bilinear of the four source-quad UV corners by (u, v).
    // The rasterizer interpolates this perspective-correctly via position.w.
    float2 top = lerp(SourceTLTR.xy, SourceTLTR.zw, corner.x);
    float2 bottom = lerp(SourceBRBL.zw, SourceBRBL.xy, corner.x);
    output.texCoord = lerp(top, bottom, corner.y);
    return output;
}

float4 psMain(vsOutput input) : SV_TARGET
{
    return InputTexture.Sample(texSampler, input.texCoord) * Color;
}
