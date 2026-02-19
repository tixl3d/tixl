#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float AddNewPoints;    
    float UseAging;
    float AgingRate;
    float MaxAge;

    float ClampAtMaxAge;
    float Reset;
    float DeltaTime;
    float ApplyMovement;

    float Speed; 
    float Drag;

    float SetInitialVelocity;
    float InitialVelocity;
}

cbuffer IntParams : register(b1) 
{
    int CollectCycleIndex;
}

StructuredBuffer<LegacyPoint> NewPoints : t0;
RWStructuredBuffer<LegacyPoint> CollectedPoints : u0;

[numthreads(64,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint newPointCount, pointStride;
    NewPoints.GetDimensions(newPointCount, pointStride);

    uint collectedPointCount, pointStride2;
    CollectedPoints.GetDimensions(collectedPointCount, pointStride2);

    uint gi = i.x;
    if(i.x >= collectedPointCount)
        return;

    if(Reset > 0.5)
    {
        CollectedPoints[gi].W =  sqrt(-1);
        return;
    }

    int addIndex = (gi - CollectCycleIndex) % collectedPointCount;

    // Insert emit points
    if( AddNewPoints > 0.5 && addIndex >= 0 && addIndex < (int)newPointCount )
    {
        CollectedPoints[gi] = NewPoints[addIndex];

        if(UseAging > 0.5) 
        {
            CollectedPoints[gi].W = 0.0001;
        }

        if(SetInitialVelocity > 0.5) 
        {
            CollectedPoints[gi].Rotation = q_encode_v(CollectedPoints[gi].Rotation, InitialVelocity);
        }
    }


    // Update other points
    else if(UseAging > 0.5 || ApplyMovement > 0.5)
    {
        if(UseAging > 0.5 ) 
        {
            float age = CollectedPoints[gi].W;

            if(!isnan(age)) 
            {    
                if(age <= 0)
                {
                    CollectedPoints[gi].W = sqrt(-1); // Flag non-initialized points
                }
                else if(age < MaxAge)
                {
                    CollectedPoints[gi].W = age+  DeltaTime * AgingRate;
                }
                else if(ClampAtMaxAge) {
                    CollectedPoints[gi].W = MaxAge;
                }
            }
        }

        if(ApplyMovement > 0.5) 
        {
            
            LegacyPoint p = CollectedPoints[gi];
            float4 rot;
            float v = q_separate_v(p.Rotation, rot);

            float3 forward =  normalize(qRotateVec3(float3(0, 0, 1), rot));

            forward *= v * 0.01 * Speed;
            p.Position += forward;

            v *= (1-Drag);
            p.Rotation = q_encode_v(rot, v);

            CollectedPoints[gi] = p;
        }
    }
}
