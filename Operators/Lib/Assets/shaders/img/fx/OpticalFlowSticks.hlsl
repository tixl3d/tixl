// this code implements this shader in hlsl, https://www.shadertoy.com/view/XlBcRV
sampler texSampler : register(s0);
Texture2D<float4> inputTextureA : register(t0);

cbuffer ParamConstants : register(b0)
{
    float Scale;
}


cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

cbuffer Resolution : register(b2)
{
    float TargetWidth;
    float TargetHeight;
}


struct vsOutput
{
    float4 position : SV_POSITION;
    float2 texCoord : TEXCOORD;
};


#define mod(x,y) (x-y*floor(x/y))


float4 psMain(vsOutput psInput) : SV_TARGET{
    float width, height;
    inputTextureA.GetDimensions(width, height);
    float2 iResolution = float2(width, height).xy;
    float blockSize = 5. + iResolution.y / Scale;
    float2 within_block = mod(psInput.position.xy, blockSize) - float2(.5 * blockSize, .5*blockSize);
    float2 block = psInput.position.xy - within_block;
    float2 uv = block.xy / iResolution.xy;
    float2 flow = inputTextureA.Sample(texSampler, uv).rg  -  float2(.5, .5);
    
    float lineness = abs(dot(normalize(flow.yx * float2(-1.,1.)), within_block));
    float alongness = (dot(flow, within_block) / blockSize);
    float dark = smoothstep(.2 * blockSize, .0, lineness) *
        step(alongness, dot(flow, flow)) * step(.0, alongness);
    float ballness = smoothstep(3., 1., dot(within_block, within_block));
    if (dot(flow, flow) < 1.e-6) {
        return float4(ballness.rr,1,  1.);
    } else {
        return float4((dark + ballness).rr, 0, 1.);
    }
}
