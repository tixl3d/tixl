#include "shared/hash-functions.hlsl"

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

cbuffer ParamConstants : register(b0)
{
    float UseVertical;
    float UseHighlights;
    float DetectionThreshold;
    float RangeThresholdOffset;

    float4 BackgroundColor;
    float4 StreakColor;

    float GradientBias;
    float ScatterThreshold;
    float Offset;
    float ScatterOffset; 

    float AddGrain;
    float MaxSteps;    
    float GradientLumaBias;
    float FadeStreaks;
}

cbuffer Resolution : register(b1)
{
    float TargetWidth;
    float TargetHeight;
}

struct Pixel {
    float4 Color;
};

RWStructuredBuffer<Pixel> ResultPoints : u0; 

float2 GetUvFromAddress(int col, int row) {
    return float2( 
            ((float)col + 0.1) / (float)TargetWidth, 
            ((float)row + 0.1) / (float)TargetHeight);
}

float GetValueFromColor(float4 color) {
    return (
        color.r * 0.299 
    + color.g * 0.587
    + color.b * 0.114);
}

int GetIndexFromAddress(int col, int row) {
    return row * TargetWidth + col; 
}

int GetIndexFromPos(int2 pos) {
    return pos.y * TargetWidth + pos.x; 
}


static int _clampedRange = 1;

static float4 _minColor;
static float4 _maxColor;
static float _minColorValue;
static float _maxColorValue;
static float4 _colorSum;

int ScanRange(int2 pos, int2 direction, float threshold) 
{
    int steps = 0;
    while(true) {

        pos += direction;
        if(steps > _clampedRange) 
            return steps;

        if(pos.x < 0 || pos.x > TargetWidth
        || pos.y < 0 || pos.y > TargetHeight)  
            return steps;

        // if(steps > 100)
        //     return steps;

        float4 c2 = inputTexture.SampleLevel(texSampler, GetUvFromAddress(pos.x, pos.y) , 0.0);
        float v2= GetValueFromColor(c2);
        if( UseHighlights > 0.5 
        ? (v2 < 1 - threshold - RangeThresholdOffset)
        : (v2 > threshold + RangeThresholdOffset)) 
             return steps;

        if(v2 > _maxColorValue) {
            _maxColorValue = v2;
            _maxColor = c2;
        }
        if(v2 < _minColorValue) {
            _minColorValue = v2;
            _minColor = c2;
        }
        _colorSum += c2;        
        steps++;
        threshold -= FadeStreaks;

    }
    return steps;
}


float GetSchlickBias(float x, float bias) {
    return x / ((1 / bias - 2) * (1 - x) + 1);
}
    


[numthreads(1,1,1)]
void main(uint3 i : SV_DispatchThreadID)
{
    uint rowIndex = i.x; 
    int2 lineDirection = UseVertical> 0.5 ? int2(0,1) : int2(1,0);
    int2 rowDirection = UseVertical> 0.5 ? int2(1,0) : int2(0,1);
    float4 maxColor = 0;

    int lineLength = UseVertical> 0.5 
        ? clamp(TargetHeight, 1, 1920*2)
        : clamp(TargetWidth, 1, 1920*2);

    int2 pos = rowDirection * rowIndex;
    float2 resolution = float2(TargetWidth, TargetHeight);

    // Clear line with original image
    for(int step = 0; step< lineLength; step++ ) {
        int2 p = pos + step * lineDirection;
        float2 uv = (p + 0.1) / resolution;
        float4 c = inputTexture.SampleLevel(texSampler, uv , 0.0);
        ResultPoints[GetIndexFromPos(p)].Color = c * BackgroundColor;
    }

    _clampedRange = clamp(MaxSteps, 1, 1920*2);


    for(int stepIndex = 0; stepIndex< lineLength; stepIndex++ , pos += lineDirection )
    {
        float hash = hash12((float2)(pos.x, pos.y));

        float2 uv = (pos + 0.1) / resolution;
            // ((float)stepIndex + 0.5) / (float)TargetWidth, 
            // ((float)rowIndex + 0.5) / (float)TargetHeight);

        float4 c = inputTexture.SampleLevel(texSampler, uv , 0.0);
        float v = GetValueFromColor(c);

        if((UseHighlights > 0.5 
            ? (v > 1-DetectionThreshold )
            : (v < DetectionThreshold ))) 
        {
            _minColor = c;
            _minColorValue = GetValueFromColor(c);

            _maxColorValue = _minColorValue;
            _maxColor = c;
            _colorSum = c;

            // Scan
            float threshold = DetectionThreshold + (hash - 0.5) * ScatterThreshold;
            int leftRangeSteps = ScanRange(pos,  -lineDirection, threshold);
            int rightRangeSteps = ScanRange(pos, +lineDirection, threshold);
            
            int rangeSteps = leftRangeSteps + rightRangeSteps +1;

            float4 averageColor = _colorSum / rangeSteps;
            //rangeSteps += 100;


            averageColor.rgb = GradientLumaBias > 0
                ? lerp(averageColor.rgb,_minColor.rgb, -GradientLumaBias)
                : lerp(averageColor.rgb,_maxColor.rgb, GradientLumaBias);
            //rangeSteps= max(rangeSteps, max(100,2 * _colorSum.r));
            
            int offsetSteps = rangeSteps * (Offset.x + (hash - 0.5) * ScatterOffset);

            // Offset range only if not on border (to avoid seems)
            int rangeStart = stepIndex - leftRangeSteps;
            if(rangeStart > 1) 
                rangeStart = clamp(rangeStart + offsetSteps,0, lineLength);

            int rangeEnd = stepIndex + rightRangeSteps;
            if(rangeEnd < lineLength) 
            {
                rangeEnd = clamp(rangeEnd + offsetSteps,0, lineLength);
            }
            else {
                rangeEnd = lineLength;
            }

            // Fill Range
            for(int rangeStepIndex = rangeStart; rangeStepIndex < rangeEnd; rangeStepIndex++ )
            {
                float randomInStreak = hash12(float2(hash + rowIndex, rangeStepIndex));
                float f = (rangeStepIndex - rangeStart) / (float)(rangeEnd - rangeStart);
                float bias = GradientBias;
                if(GradientBias < 0) {
                    f = 1 - f;
                }
                f = saturate(f + (randomInStreak-0.5) * AddGrain);
                //saturate(f)
                //f = fmod(f ,1);

                
                //averageColor = lerp(averageColor, _minColor, 1);
                
                float4 streakColor = f < 0.5 
                    ? lerp(_minColor, averageColor, GetSchlickBias(f * 2, abs(GradientBias)) )
                    : lerp(_maxColor, averageColor, GetSchlickBias(1- (f -0.5) * 2, abs(GradientBias)));
                streakColor.rgb *= StreakColor.rgb;

                int2 rangePos = rowDirection * rowIndex + lineDirection * rangeStepIndex; 
                    //rowIndex + (int)(Offset.y * TargetHeight));
                int address= GetIndexFromAddress(rangePos.x, rangePos.y);
                float4 orgColor = ResultPoints[address].Color;
                ResultPoints[address].Color =  lerp(streakColor, orgColor, (1-StreakColor.a) );
            }
            stepIndex+= rightRangeSteps;
            pos += rightRangeSteps * lineDirection;
        }        
    }
}

