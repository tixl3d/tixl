cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};

Texture2D<float4> Image : register(t0);
sampler Sampler : register(s0);

#define PI 3.14159265359

float4 psMain(vsOutput input) : SV_TARGET
{
    float width, height;
    Image.GetDimensions(width, height);
    float4 c=float4(1,1,0,1);

    float2 uv = input.texCoord;

    // from Bartosz
    // https://stackoverflow.com/questions/34250742/converting-a-cubemap-into-equirectangular-panorama

    float phi = input.texCoord.x * PI * 2;
    float theta = input.texCoord.y * PI;

    float x = sin(phi) * sin(theta) * -1;
    float y = cos(theta);
    float z = cos(phi) * sin(theta) * -1;

    float a = max(abs(x), max(abs(y), abs(z)));
    float xa = x / a;
    float ya = y / a;
    float za = z / a;

    c.rgb = float3(x,y,z);

    if (abs(xa - 1.0) < 1e-3f) // Use consistent epsilon
    {
      uv = (float2(-za, -ya)+1)/2;
      uv.x /= 6;
      uv.x += 0./6.;
      c.rgb = Image.Sample(Sampler, uv);
    }
    else if (abs(xa+1.) < 1e-3f)
    {
      uv = (float2(za, -ya)+1)/2;
      uv.x /= 6;
      uv.x += 2./6.;
      c.rgb = Image.Sample(Sampler, uv);
    }
    else if (abs(za-1.) < 1e-3f)
    {
      uv = (float2(xa, -ya)+1)/2;
      uv.x /= 6;
      uv.x += 3./6.;
      c.rgb = Image.Sample(Sampler, uv);
    }
    else if (abs(za+1.) < 1e-3f)
    {
      uv = (float2(-xa, -ya)+1)/2;
      uv.x /= 6;
      uv.x += 1./6.;
      c.rgb = Image.Sample(Sampler, uv);
    }
    else if (abs(ya-1.) < 1e-3f)
    {
      uv = (float2(-xa, -za)+1)/2;
      uv.x /= 6;
      uv.x += 4./6.;
      c.rgb = Image.Sample(Sampler, uv);
    }
    else if (abs(ya+1.) < 1e-3f)
    {
      uv = (float2(xa, -za)+1)/2;
      uv.x /= 6;
      uv.x += 5./6.;
      c.rgb = Image.Sample(Sampler, uv);
    }
    
    return c;
}