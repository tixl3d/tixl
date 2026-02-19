    // SeparableBlurPS.hlsl
    // Performs one pass (horizontal or vertical) of a Gaussian blur
    // using 5 bilinear samples to approximate a 9-tap kernel.

    // Constant Buffer - must match C# struct layout
    cbuffer BlurParams : register(b0)
    {
        float DirX;         // Pixel offset X for blur direction (e.g., BlurOffset or 0)
        float DirY;         // Pixel offset Y for blur direction (e.g., 0 or BlurOffset)
        float Width;        // Input texture width for this pass
        float Height;       // Input texture height for this pass
        int UseMask;        // (Not used by Bloom internal blur)
        int MaskInvert;     // (Not used by Bloom internal blur)
        int ClampTexture;   // bool: Clamp result color?
        int _padding0;      // Padding
    };

    // Input Texture for this blur pass
    Texture2D InputTexture : register(t0);
    // Sampler State (Linear is required for the multi-tap approximation)
    SamplerState LinearSampler : register(s0);

    // Input structure from Vertex Shader
    struct PS_INPUT 
    {
        float4 pos : SV_POSITION;
        float2 uv  : TEXCOORD0;
    };

    // Gaussian weights and offsets for 5 linear samples
    static const float O[3] = { 0.0, 1.3846153846, 3.2307692308 }; // Sample Offsets (in pixels)
    static const float W[3] = { 0.2270270270, 0.3162162162, 0.0702702703 }; // Sample Weights

    // Pixel Shader Main Function
    float4 psMain(PS_INPUT input) : SV_Target
    {
        // Calculate texel size for the current texture dimensions
        float2 texelSize = float2(1.0 / Width, 1.0 / Height);

        // Calculate the blur direction vector scaled by texel size (UV space offset per pixel)
        float2 blurVec = float2(DirX, DirY) * texelSize;

        // Sample the input texture (center tap)
        float4 blurredColor = InputTexture.Sample(LinearSampler, input.uv) * W[0]; // Apply center weight

        // Add weighted samples +/- offset 1
        blurredColor += InputTexture.Sample(LinearSampler, input.uv + blurVec * O[1]) * W[1];
        blurredColor += InputTexture.Sample(LinearSampler, input.uv - blurVec * O[1]) * W[1];

        // Add weighted samples +/- offset 2
        blurredColor += InputTexture.Sample(LinearSampler, input.uv + blurVec * O[2]) * W[2];
        blurredColor += InputTexture.Sample(LinearSampler, input.uv - blurVec * O[2]) * W[2];

        // Optional Clamping
        if (ClampTexture > 0)
        {
            blurredColor = saturate(blurredColor);
        }

        return blurredColor;
    }
    