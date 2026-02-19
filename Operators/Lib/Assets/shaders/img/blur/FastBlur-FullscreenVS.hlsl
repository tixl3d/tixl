struct VS_OUTPUT
{
    float4 position : SV_POSITION;
    float2 uv : TEXCOORD0;
};

VS_OUTPUT vsMain(uint vertexId : SV_VertexID)
{
    VS_OUTPUT o;
    o.uv = float2((vertexId << 1) & 2, vertexId & 2);
    o.position = float4(o.uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return o;
}