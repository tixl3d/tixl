#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float WidthF;
    float HeightF;
    float HistoryStepsF;
    float NumStates;

    float NeighbourCountF; //4
    float MousePosX;
    float MousePosY;
    float MouseDown;

    float Reset;  // 8
    float RandomSeed;
    float FxThreshold;
}

// cbuffer TimeConstants : register(b1)
// {
//     float globalTime;
//     float time;
//     float runTime;
//     float beatTime;
// }

// struct Cell {
//     int State;
// };

Texture2D<float4> GradientTexture : register(t0);
Texture2D<float4> FxTexture : register(t1);
sampler texSampler : register(s0);

RWStructuredBuffer<int> ReadField : register(u0); 
RWStructuredBuffer<int> WriteField : register(u1); 

RWStructuredBuffer<int> TransitionFunctions : register(u2); 
RWTexture2D<float4> WriteOutput  : register(u3); 

static const int2 NeighbourOffsets[] = 
{
  int2( 0, -1),
  int2( -1,  0),
  int2(  0,  0),
  int2( +1,  0),
  int2( 0,  +1),

  int2( -1,  -1),
  int2( +1,  -1),
  int2( -1,  +1),
  int2( +1,  +1),
};

[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{     
    int2 pos = i.xy;
    int3 rez = int3((int)WidthF, (int)HeightF, (int)HistoryStepsF  );
    int pInFieldBuffer = i.x + rez.x * i.y;

    int s = ReadField[pInFieldBuffer.x];
    // Mouse
    if(MousePosX >= 0 && MousePosX < 1
    && MousePosY >=0 && MousePosY < 1) {
        int mousePosInField = MousePosX* WidthF + (int)(MousePosY * HeightF) * WidthF;
        if(abs(mousePosInField - pInFieldBuffer) < 4) {
            WriteOutput[i.xy] = float4(1,0,0,1);
            if(MouseDown > 0.5) 
            {
                s=0;
                ReadField[pInFieldBuffer] = 10;
            }
        }
    }

    if(Reset > 0.5) 
    {
        s =  (int)(hash11(i.x * 12.3 + i.y * 1239.7+ RandomSeed) * NumStates);
        WriteField[pInFieldBuffer] = s;
        return;
    }

    {

        float4 fxRgb = FxTexture.SampleLevel(texSampler, pos / float2(WidthF, HeightF), 0);
        float fx = fxRgb.r * fxRgb.a;
        
        if(fx > FxThreshold) 
            ReadField[pInFieldBuffer] = (fx - FxThreshold) / (1 - FxThreshold) * NumStates;

    }

    // Permanent seed
    // {
    //     bool isInCenter = abs(i.x - WidthF/3) < 10;
    //     s = isInCenter ? (int)(hash11(i.x + RandomSeed) * NumStates)
    //                     : s;
    // }

    int requiredBitCount = (int)(ceil(log2((float)NumStates)));
    int mask = (1 << requiredBitCount) -1;    // Just to make sure. Actually this should be required.

    int lookupResult = 0;
    int NeighbourCount = clamp((int)NeighbourCountF,1,9);
    
    int offset = NeighbourCount < 5 ? 1:0;

    for(int nIndex = 0; nIndex < NeighbourCount; nIndex++) 
    {

        lookupResult = lookupResult << requiredBitCount;
        int2 offsetXY= NeighbourOffsets[nIndex + offset];

        int nPos = pInFieldBuffer + offsetXY.x + rez.x * offsetXY.y;

        // if(xy < 0) {
        //     xy += rez.xy;
        // }
        // else if(xy >= rez.xy) {
        //     xy -= rez.xy;
        // }

        lookupResult+= ReadField[nPos];
    }
    s = TransitionFunctions[lookupResult];
    WriteField[pInFieldBuffer] =  s;

    AllMemoryBarrier();

    float value = (float)s/NumStates;
    WriteOutput[i.xy] = GradientTexture.SampleLevel(texSampler,float2(value,0),0);
}
