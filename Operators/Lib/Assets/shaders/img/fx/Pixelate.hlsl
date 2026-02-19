//Pixelate shader by Newemka

// Constant buffer for parameters, bound to register b0
cbuffer ParamConstants : register(b0)
{
    float4 Color;       // Multiplier color applied to the final output
    float Divisor;      // Divisor used to determine tile size
    float2 TileAmount;  // Number of tiles in x and y directions
}

// Constant buffer for screen resolution, bound to register b1
cbuffer Resolution : register(b1)
{
    float TargetWidth;   // Width of the target screen
    float TargetHeight;  // Height of the target screen
}

// Structure for vertex shader output, used as input for the pixel shader
struct vsOutput
{
    float4 position : SV_POSITION;  // Position in screen space
    float2 texCoord : TEXCOORD;     // Texture coordinates
};

// Textures and sampler declarations
Texture2D<float4> Image : register(t0);   // Image texture, bound to register t0
Texture2D<float4> Shape : register(t1);   // Shape texture, bound to register t1
sampler texSampler : register(s0);        // Sampler for texture sampling, bound to register s0

// Main pixel shader function
float4 psMain(vsOutput input) : SV_TARGET
{
    // Get the dimensions of the image texture
    float width, height;
    Image.GetDimensions(width, height);
    float2 resolution = float2(width, height);

    // Initialize texture coordinates
    float2 uv = input.texCoord;
    float2 uv1 = input.texCoord;
    float divisor = Divisor * 2.0; // We make sure the divisor is always a multiple of 2
    
    // Determine the size of each tile based on the divisor
    float2 tileSize = 1.0; // same as float2 tileSize = float2(1.0,1.0); 
    float2 dimensions = floor(resolution / divisor);
    
    // If the divisor is greater than 0.5, use dimensions for tiling
    if (Divisor > 0.5)
    {
        tileSize = 1.0 / dimensions;
        uv1 *= dimensions;
    }
    // Otherwise, use the TileAmount for tiling
    else
    {
        tileSize = 1.0 / TileAmount;
        uv1 *= TileAmount;
    }

    // Calculate the fractional part of the texture coordinates for tiling
    float2 gv = frac(uv1);
    //return float4(gv,0,1); // Check the uv coordinates, try with uv1 instead of gv ;)  

    // Sample the shape texture with the adjusted coordinates, we repeat the shape on each tile
    float4 tileShape = Shape.Sample(texSampler, gv);


    // Adjust the texture coordinates for tiling
    uv = floor(uv / tileSize) * tileSize + tileSize * 0.5;
    
    // Sample the image texture with the original coordinates
    float4 imageColor = Image.SampleLevel(texSampler, uv, 0); //we use SampleLevel so we don't get weird effect on the borders of the tiles
    
    // Return the final color by combining the shape, image color, and the multiplier color
    return tileShape * imageColor * Color;
}
