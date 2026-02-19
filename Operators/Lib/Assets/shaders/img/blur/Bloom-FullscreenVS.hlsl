    // FullscreenVS.hlsl
    // Generates a fullscreen triangle and passes UV coordinates to the pixel shader.

    // Output structure for the vertex shader / Input for the pixel shader
    struct VS_OUTPUT
    {
        float4 position : SV_POSITION; // Clip space position
        float2 uv       : TEXCOORD0;   // Texture coordinates
    };

    // Vertex Shader Main Function
    // Uses SV_VertexID to generate vertices for a fullscreen triangle.
    VS_OUTPUT vsMain(uint vertexId : SV_VertexID)
    {
        VS_OUTPUT output;
        // Generate UV coordinates based on vertex ID
        // Creates coordinates: (0,0), (2,0), (0,2)
        output.uv = float2((vertexId << 1) & 2, vertexId & 2);

        // Generate clip space positions based on UVs
        // Maps UVs (0,0) -> (-1, 1), (2,0) -> (3, 1), (0,2) -> (-1,-3)
        // This covers the entire screen clip space (-1 to 1 in X and Y)
        output.position = float4(output.uv * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);

        return output;
    }
    