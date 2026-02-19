#include "shared/hash-functions.hlsl"

cbuffer ParamConstants : register(b0)
{
    float WidthF;
    float HeightF;
    float HistoryStepsF;
    float NumStates;
    float NeighbourCountF;
    float MousePosX;
    float MousePosY;
    float MouseDown;
    float Reset;
    float RandomSeed;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
}

struct Cell {
    int State;
};

Texture2D<float4> GradientTexture : register(t0);
sampler texSampler : register(s0);

RWStructuredBuffer<Cell> ReadField : register(u0); 
RWStructuredBuffer<Cell> WriteField : register(u1); 

RWStructuredBuffer<Cell> TransitionFunctions : register(u2); 
RWTexture2D<float4> WriteOutput  : register(u3); 

static const int2 NeighbourOffsets[] = 
{
  int2( -2,  0),
  int2( -1,  0),
  int2(  0,  0),
  int2( +1,  0),
  int2( +2,  0),
};

//static const int NeighbourCount = 5;

[numthreads(16,16,1)]
void main(uint3 i : SV_DispatchThreadID)
{     
    int2 pos = i.xy;
    int3 rez = int3((int)WidthF, (int)HeightF, (int)HistoryStepsF  );
    int pInFieldBuffer = i.x + rez.x * i.y;

    int s = ReadField[pInFieldBuffer].State;


    
    {

    }
    // Mouse
    // if(MousePosX >= 0 && MousePosX < 1
    // && MousePosY >=0 && MousePosY < 1) {
    //     int mousePosInField = MousePosX* WidthF + (int)(MousePosY * HeightF) * WidthF;
    //     if(abs(mousePosInField - pInFieldBuffer) < 4) {
    //         WriteOutput[i.xy] = float4(1,0,0,1);
    //         if(MouseDown > 0.5) 
    //         {
    //             s=0;
    //             ReadField[pInFieldBuffer].State = 1;
    //         }
    //     }
    // }

    if(Reset > 0.5) 
    {
        int s= 0;
        if(i.y == 0) 
        {
            bool isInCenter = abs(i.x - WidthF/3) < 5;
            s = isInCenter ? (int)(hash11(i.x + RandomSeed) * NumStates)
                            :0;

        }
        WriteField[pInFieldBuffer].State =  s;
        return;
    }


    // Simulate first line
    if(i.y == 0) 
    {
        // Permanent seed
        {
            bool isInCenter = abs(i.x - WidthF/3) < 10;
            s = isInCenter ? (int)(hash11(i.x + RandomSeed) * NumStates)
                            : s;
        }

        int requiredBitCount = (int)(ceil(log2((float)NumStates)));
        int mask = (1 << requiredBitCount) -1;    // Just to make sure. Actually this should be required.

        int lookupResult = 0;
        int NeighbourCount = clamp((int)NeighbourCountF,1,5);
        
        int offset = NeighbourCount < 5 ? 1:0;

        for(int nIndex = 0; nIndex < NeighbourCount; nIndex++) 
        {

            lookupResult = lookupResult << requiredBitCount;
            int x = (pInFieldBuffer + NeighbourOffsets[nIndex + offset].x);

            if(x < 0) {
                x += rez.x;
            }
            else if(x >= rez.x) {
                x -= rez.x;
            }

            lookupResult+= ReadField[x].State;// & mask; 
        }
        s = TransitionFunctions[lookupResult].State;

        WriteField[pInFieldBuffer].State =  s;
    }
    // Copy to history 
    else {
        s = ReadField[pInFieldBuffer - rez.x].State;
        WriteField[pInFieldBuffer].State =  ReadField[pInFieldBuffer - rez.x].State;
    }
    AllMemoryBarrier();

    float value = (float)s/NumStates;
    WriteOutput[i.xy] = GradientTexture.SampleLevel(texSampler,float2(value,0),0);
}
