Texture2D<float> InputTexture : register(t0);
RWTexture2D<float> OutputTexture : register(u0);

cbuffer ParamConstants : register(b0)
{
    float Near;
    float Far;
    float OutrangeMin;
    float OutrangeMax;
    float ClampRange;
    float Mode;
}
[numthreads(16, 16, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint width, height;
    InputTexture.GetDimensions(width, height);
    if (i.x >= width || i.y >= height)
        return;

    float n = Near;
    float f = Far;
    float depth = InputTexture[i.xy].r;

    if (depth < 0)
    {
        OutputTexture[i.xy] = (i.x + i.y) % 16 > 0 ? 0 : 1;
        return;
    }

    float c = Mode < 0.5
                  ? (-f * n) / (depth * (f - n) - f)
                  : (c = (2.0 * n) / (f + n - depth * (f - n))); // Legacy Mode for Depth of Field

    if (OutrangeMin != 0 || OutrangeMax != 0)
    {
        c = (c - OutrangeMin) / (OutrangeMax - OutrangeMin);
    }

    OutputTexture[i.xy] = ClampRange > 0.5 ? saturate(c) : c;
}
