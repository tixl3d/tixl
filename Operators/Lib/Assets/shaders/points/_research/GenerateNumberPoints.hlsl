#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float Scale;
    float Spacing;
    float LineWidth;
}

cbuffer IntParams : register(b1)
{
    int DigitCount;
    int PointsPerChar;
    int TargetPointCount;
    int NumericValuesCount;

    int NumberMode;
    int FloatDigits;
    int Increment;
}

StructuredBuffer<Point> TargetPoints : register(t0); 
StructuredBuffer<Point> DigitCharPoints : register(t1);   
StructuredBuffer<int> IntValues : register(t2);   

RWStructuredBuffer<Point> ResultPoints : register(u0); 

static const int Pow10Table[10] =
{
    1,
    10,
    100,
    1000,
    10000,
    100000,
    1000000,
    10000000,
    100000000,
    1000000000
};


// Returns:
// 0..9  = digit
// 10    = blank " "
// 11    = minus "-"


#define CPeriod 12
#define CMinus 11
#define CSpace 10

int GetDigitOrSymbol(int v, int n)
{
    int absValue = abs(v);

    // Special case for zero: show "0" only at position 0
    if (absValue == 0)
        return n == 0 ? 0 : CSpace;

    // Normal digit exists at this position
    if (n < 10 && absValue >= Pow10Table[n])
        return (absValue / Pow10Table[n]) % 10;

    // Position just beyond the most significant digit: show minus for negatives
    if (v < 0 && n < 9 && absValue < Pow10Table[n] && absValue >= Pow10Table[n - 1 < 0 ? 0 : n - 1])
        return CMinus;

    // Everything further left is blank
    return CSpace;
}

int GetDigitOrSymbolFloat(float v, int n)
{
    int sg= v<0 ? - 1:1;
    int absValue = (floor(abs(v)));
    int absValue2 = (round(frac(abs(v))*pow(10,FloatDigits)));
    if(n==FloatDigits)
        return CPeriod;

    int cf=GetDigitOrSymbol(absValue2,n);

    if(cf==CSpace)
        cf=0;

    return n>FloatDigits
        ? (absValue == 0 && sg < 0) 
            ? (n==FloatDigits+1 ? 0 : (n>FloatDigits+2 ? CSpace : CMinus))
            : (GetDigitOrSymbol(absValue*sg,n-FloatDigits - 1))
    :cf;
}




[numthreads(64, 1, 1)] void main(uint3 i : SV_DispatchThreadID)
{
    uint idx = i.x;

    int value = IntValues[idx % NumericValuesCount];

    uint pointsPerTarget = (DigitCount * PointsPerChar);
    uint targetIndex = idx / pointsPerTarget;
    uint indexInTarget = idx % pointsPerTarget;
    
    int digitIndex = indexInTarget / PointsPerChar;

    float fnumber = 0;
    
    if(NumberMode == 0) 
    {
        fnumber = IntValues[targetIndex % NumericValuesCount];
    }
    else if(NumberMode== 1) 
    {
        fnumber = TargetPoints[targetIndex].FX1;
    }
    else if(NumberMode == 2)
    {
        fnumber = TargetPoints[targetIndex].FX2;
    }
    else 
    {
        fnumber = targetIndex;
    }

    fnumber += (int)targetIndex * Increment;

    //int number = int(fnumber);
    //number = TargetPoints[targetIndex].Position.y * 1000;
    //int number = targetIndex;

    int digitCharIndex = digitIndex%10; // hack to test.
    
    int char = GetDigitOrSymbolFloat(fnumber, digitIndex);

    int charPointStart = char * PointsPerChar;
    int indexInChar = idx % PointsPerChar;


    Point p = DigitCharPoints[charPointStart + indexInChar];

    float3 pos = p.Position;
    pos.x -= digitIndex * Spacing;
    pos *= TargetPoints[targetIndex].Scale * Scale; 
    
    float3 posInWorld = qRotateVec3(pos,TargetPoints[targetIndex].Rotation);

    posInWorld +=  TargetPoints[targetIndex].Position;
    p.Position = posInWorld;

    p.Color *= TargetPoints[targetIndex].Color;
    p.Scale *= TargetPoints[targetIndex].Scale * LineWidth;

    ResultPoints[idx] = p;
}
