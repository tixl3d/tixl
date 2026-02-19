//RWTexture2D<float4> outputTexture : register(u0);
Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float4 EdgeColor;
    float4 Background;


    float Scale;
    float LineWidth;
    float Phase;
    
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




// The MIT License
// Copyright © 2013 Inigo Quilez
// Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software. THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

// I've not seen anybody out there computing correct cell interior distances for Voronoi
// patterns yet. That's why they cannot shade the cell interior correctly, and why you've
// never seen cell boundaries rendered correctly. 
//
// However, here's how you do mathematically correct distances (note the equidistant and non
// degenerated grey isolines inside the cells) and hence edges (in yellow):
//
// http://www.iquilezles.org/www/articles/voronoilines/voronoilines.htm
//
// More Voronoi shaders:
//
// Exact edges:  https://www.shadertoy.com/view/ldl3W8
// Hierarchical: https://www.shadertoy.com/view/Xll3zX
// Smooth:       https://www.shadertoy.com/view/ldB3zc
// Voronoise:    https://www.shadertoy.com/view/Xd23Dh

//#define ANIMATE

static float aspectRatio = 1;


float4 sampleTexture( float2 p )
{
    return inputTexture.Sample(texSampler, (p+0.5)/Scale / float2(aspectRatio,1));
}

float3 voronoi( in float2 x, out float4 color )
{
    float2 n = float2(floor(x.x), floor(x.y));
    
    float2 f = mod(x, 1.00);

    //----------------------------------
    // first pass: regular voronoi
    //----------------------------------
    float2 mg, mr;

    color = 1;

    float md = 8.0;
    {
        for( int j=-1; j<=1; j++ )
        for( int i=-1; i<=1; i++ )
        {
            float2 g = float2(float(i),float(j));
            float2 o = sampleTexture( n + g ).xy;
            o = 0.5 + 0.5*sin( Phase + 6.2831 * o );
            
            float2 r = g + o - f;
            float d = dot(r,r);

            if( d<md )
            {
                md = d;
                mr = r;
                mg = g;
            }
        }
    }

    //----------------------------------
    // second pass: distance to borders
    //----------------------------------
    md = 8.0;
    
    float2 g=0;
    for( int j=-2; j<=2; j++ )
    for( int i=-2; i<=2; i++ )
    {
        g = mg + float2(float(i),float(j));
        color = sampleTexture(n + g);
        float2 o = color.xy;

        o = 0.5 + 0.5*sin( Phase + 6.2831*o ).xy;

        float2 r = g + o - f;

        if( dot(mr-r,mr-r)>0.00001 )
        md = min( md, dot( 0.5*(mr+r), normalize(r-mr) ) );
    }
    color = sampleTexture(n - 2 +g);
    
    return float3( md, mr );
}

void mainImage( out float4 fragColor, in float2 fragCoord )
{

}


float4 psMain(vsOutput psInput) : SV_TARGET
{
    float2 uv = psInput.texCoord;
    float4 inputColor = inputTexture.Sample(texSampler, uv);
    aspectRatio = TargetWidth / TargetHeight;
    
    uv.x *= aspectRatio;

    float2 p = uv * Scale;
    float4 cellColor;
    float3 c = voronoi( p, cellColor );
    float3 col =  Background.rgb * cellColor.rgb;

    col = lerp( EdgeColor.rgb, col, smoothstep( 0.04,  0.07,  c.x - LineWidth * 0.1 + 0.1  ) );
    return float4(col, 1.0);
}
