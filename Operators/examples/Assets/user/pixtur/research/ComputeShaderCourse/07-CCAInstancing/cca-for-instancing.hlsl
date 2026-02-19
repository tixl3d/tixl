#include "shared/hash-functions.hlsl"


cbuffer ParamConstants : register(b0)
{
    float Threshold;
    float MaxStates;
    float Range;
    float RandomAmount;
    float DoCalculateStep;
    float BlendLastStepFactor;

    float R_xThreshold;
    float G_xStates;
    float UseMooreRegion;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}


cbuffer TimeConstants : register(b2)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct Pixel {
    float4 Color;
};

Texture2D<float4> FxTexture : register(t0);
sampler texSampler : register(s0);

RWStructuredBuffer<Pixel> ReadPoints : register(u0); 
RWStructuredBuffer<Pixel> WritePoints : register(u1); 
RWTexture2D<float4> WriteOutput  : register(u2); 

static const int2 DirectionsMoore[] = 
{
  int2( -1,  0),
  int2( -1, +1),
  int2(  0, +1),
  int2( +1, +1),
  int2( +1,  0),
  int2( +1, -1),
  int2(  0, -1),
  int2( -1, -1),
};

static const int2 DirectionsNeumann[] = 
{
  int2( -1,  0),
  int2( +1,  0),
  int2(  0, -1),
  int2(  0, +1),
};


[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{         
    int _maxRangeSteps;
    int2 _centerPos;
    int2 _screenSize;
    int _maxStates;
    int _threshold;

    _screenSize = int2(TargetWidth, TargetHeight);
    _centerPos = i.xy; 
    float2 uv = _centerPos / (float2)_screenSize;
    float4 fx = FxTexture.SampleLevel(texSampler, uv, 0.0);

    _threshold = (int)(Threshold + 0.3 + fx.r * R_xThreshold);
    _maxStates = (int)(MaxStates + 0.3 + fx.g * G_xStates);
    
    float4 c = ReadPoints[i.x+ i.y * TargetWidth].Color;    
    float _randomAmoundFx = RandomAmount + fx.b;
    // if(fx.b > 0.02) {
    //     c.r = fx.b * _maxStates;
    // }

    if(_randomAmoundFx>0 ) 
    {
        bool isInitialized = c.a > 0.5;

        float hash = hash12( uv * 431 + 111 );
        bool shouldFill = hash < _randomAmoundFx;
        
        if(shouldFill || !isInitialized) 
        {
            c = float4((int)(hash * _maxStates),0,0,1);
            WritePoints[i.x + i.y * TargetWidth].Color = c;
            ReadPoints[i.x + i.y * TargetWidth].Color = c;
        }
    }

    if(DoCalculateStep) 
    {
        _maxRangeSteps = clamp(Range, 1,100);

        int tc= (int)(c.r);
        c.b = c.r; // save last state for blending

        int sum =0;
        int next = (uint)(tc + 1) % _maxStates;
        //int prev = (tc - 1 + _maxStates) % _maxStates;

        if(UseMooreRegion > 0.5) {
            for(int directionIndex = 0; directionIndex < 4; directionIndex ++)
            {
                int2 direction = DirectionsNeumann[directionIndex];
                int2 pos = _centerPos;
                for(int step=0; step < _maxRangeSteps; step++) 
                {
                    pos += direction;
                    float4 neighbourColor = ReadPoints[pos.x + pos.y *TargetWidth].Color;
                    int t = (int)(neighbourColor.r + 0.1 % _maxStates);
                    sum+= (t == next) ? 1:0;    
                } 
            }

        }
        else {
            for(int directionIndex = 0; directionIndex < 8; directionIndex ++)
            {
                int2 direction = DirectionsMoore[directionIndex];
                int2 pos = _centerPos;
                for(int step=0; step < _maxRangeSteps; step++) 
                {
                    pos += direction;
                    float4 neighbourColor = ReadPoints[pos.x + pos.y *TargetWidth].Color;
                    int t = (int)(neighbourColor.r + 0.1 % _maxStates);
                    sum+= (t == next) ? 1:0;    
                } 
            }
        }

        if(sum >= _threshold)
        {            
            c.r++;
        }
        if(c.r >= _maxStates) 
        {
            c.r =0;
        }
        c.g = c.r;
        WritePoints[i.x + i.y * TargetWidth].Color = c;     
    }
    else {
        //WritePoints[i.x + i.y * TargetWidth].Color = c;
    }
    
    float result = c.r / _maxStates; // lerp(c.b, c.g, BlendLastStepFactor) / _maxStates;
    WriteOutput[i.xy] = float4(result,result,result,1);
}
