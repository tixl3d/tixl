    // DownsamplePS.hlsl
    // Samples a 2x2 area in the source texture and averages the result.
    // Assumes rendering to a render target half the width and height.

    // Input Texture (higher resolution)
    Texture2D SourceTexture : register(t0);
    // Sampler State (Linear is needed for good quality downsampling)
    SamplerState LinearSampler : register(s0);

    // Input structure from Vertex Shader
    struct PS_INPUT
    {
        float4 pos : SV_POSITION;
        float2 uv  : TEXCOORD0;
    };

    // Pixel Shader Main Function
    float4 psMain(PS_INPUT input) : SV_Target
    {
        // Get dimensions of the source texture to calculate texel size
        float width, height;
        SourceTexture.GetDimensions(width, height);
        float2 texelSize = float2(1.0f / width, 1.0f / height);

        // Sample a 2x2 box filter around the source UV corresponding to this destination pixel center.
        // The input UV corresponds to the center of the target pixel.
        // We need to sample the four source texels that contribute to this target pixel.
        float2 uv00 = input.uv + texelSize * float2(-0.5f, -0.5f); // Top-left texel center in source
        float2 uv10 = input.uv + texelSize * float2( 0.5f, -0.5f); // Top-right
        float2 uv01 = input.uv + texelSize * float2(-0.5f,  0.5f); // Bottom-left
        float2 uv11 = input.uv + texelSize * float2( 0.5f,  0.5f); // Bottom-right

        float4 color = 0;
        color += SourceTexture.Sample(LinearSampler, uv00);
        color += SourceTexture.Sample(LinearSampler, uv10);
        color += SourceTexture.Sample(LinearSampler, uv01);
        color += SourceTexture.Sample(LinearSampler, uv11);

        return color * 0.25f; // Average the 4 samples

        // Alternative: Single bilinear sample at the center can also work well for 2x downsample.
        // return SourceTexture.Sample(LinearSampler, input.uv);
    }
    