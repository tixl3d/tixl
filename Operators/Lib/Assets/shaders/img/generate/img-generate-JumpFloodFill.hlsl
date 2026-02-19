sampler TextureInSampler : register(s0);
Texture2D<float4> TextureIn : register(t0);

RWTexture2D<float4> BufferA : register(u0);
RWTexture2D<float4> BufferB : register(u1);

cbuffer Params : register(b0)
{
    int LastIter;
    int LastIterPow;
    int MaxItersIsEven;
    int CurrentIter;
    int CurrentIterPow;
};

#define OFFSETS_COUNT 9

static int2 OFFSETS[OFFSETS_COUNT] =
{
    int2(-1, -1), int2(0, -1), int2(1, -1),
    int2(-1,  0), int2(0,  0), int2(1,  0),
    int2(-1,  1), int2(0,  1), int2(1,  1),
};

#define DIST_LARGE 100000000000.0

[numthreads(16, 16, 1)]
void main(uint3 threadId : SV_DispatchThreadID)
{
    int sizeWidth, sizeHeight;
    BufferA.GetDimensions(sizeWidth, sizeHeight);
    int2 size = int2(sizeWidth, sizeHeight);
    int2 currentCoord = threadId.xy;
    if(currentCoord.x > size.x || currentCoord.y > size.y) return;

    float4 currentSample = float4(0.0, 0.0, 0.0, 0.0);

    // src -> a
    // a   -> b
    // b   -> a

    bool sourceBufferIsB = CurrentIter % 2 == MaxItersIsEven;

    // iter 0 - source is always the texture
    if(CurrentIter == 0)
    {
        float2 currentUv = (currentCoord + float2(0.5, 0.5)) / size;
        float currentAlpha = TextureIn.SampleLevel(TextureInSampler, currentUv, 0).a > 0.0;
        currentSample = float4(currentAlpha * currentCoord, 0.0, currentAlpha);
    }
    else
    {
        // target is A so source is B
        int2 closestSeedCoord = int2(0, 0);
        float closestSeedDistSq = DIST_LARGE;
    
        for(int offsetIndex = 0; offsetIndex < OFFSETS_COUNT; offsetIndex++)
        {
            int2 offsetCoord = currentCoord + OFFSETS[offsetIndex] * CurrentIterPow;
            offsetCoord = clamp(offsetCoord, int2(0, 0), size - int2(1, 1));
            
            float4 offsetSample = sourceBufferIsB ? BufferB[offsetCoord] : BufferA[offsetCoord];

            bool offsetSeedValid = saturate(offsetSample.a) > 0.0;
            int2 offsetSeedCoord = int2(offsetSample.rg);
            int2 offsetSeedDisplacement = offsetSeedCoord - currentCoord;
            float offsetSeedDistSq = dot(offsetSeedDisplacement, offsetSeedDisplacement);

            bool offsetSeedUse = offsetSeedValid && closestSeedDistSq > offsetSeedDistSq;

            closestSeedDistSq = offsetSeedUse ? offsetSeedDistSq : closestSeedDistSq;
            closestSeedCoord = offsetSeedUse ? offsetSeedCoord : closestSeedCoord;
        }
        
        bool closestSeedValid = closestSeedDistSq < DIST_LARGE;

        currentSample = float4(closestSeedCoord, 0.0, closestSeedValid);
    }

    if(CurrentIter == LastIter)
    {
        // final pass, get the distance
        float currentLength = (1.0 - (length(currentSample.rg - currentCoord) / LastIterPow)) * currentSample.a;
        BufferA[currentCoord] = float4(currentLength, currentLength, currentLength, currentSample.a);
    }
    else if(sourceBufferIsB)
    {
        BufferA[currentCoord] = currentSample;
    }
    else
    {
        BufferB[currentCoord] = currentSample;
    }
}