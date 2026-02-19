#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

static const float3 Corners[] =
    {
        float3(-1, -1, 0),
        float3(1, -1, 0),
        float3(1, 1, 0),
        float3(1, 1, 0),
        float3(-1, 1, 0),
        float3(-1, -1, 0),
};

static const uint digits[5] = {
    989790207, // 0b111 010 111 111 101 111 111 111 111 111 ,
    707184749, // 0b101 010 001 001 101 100 100 001 101 101 ,
    721420031, // 0b101 010 111 111 111 111 111 011 111 111 ,
    713333353, // 0b101 010 100 001 001 001 101 001 101 001 ,
    989658751, // 0b111 010 111 111 001 111 111 001 111 111
};

static const uint characters[5] = {
    //  -
    14680064,   // 0b000 000 111 000 000 000 000 000 000 000,
    48234496,   // 0b000 010 111 000 000 000 000 000 000 000,
    1038090240, // 0b111 101 111 000 000 000 000 000 000 000,
    48234496,   // 0b000 010 111 010 000 000 000 000 000 000,
    15204352,   // 0b000 000 111 010 000 000 000 000 000 000
};

static const uint MinusCharIndex = 0;
static const uint NanCharIndex = 1;

static const int DigitCount = 8;
static const int LabelPadding = 1;
static const int2 DigitSize = int2(3, 5);
static const int DigitPadding = 1;
static const int2 PaddedDigitSize = DigitSize + DigitPadding;
static const int2 LabelSize = int2(DigitCount, 1) * PaddedDigitSize + 2 * LabelPadding - 1;

cbuffer Params : register(b0)
{
    float4 Color;
    float2 LabelOffset;
    float Scale;
};

cbuffer IntParams : register(b1)
{
    int StartIndex;
    int ShowMode;
};

cbuffer Transforms : register(b2)
{
    float4x4 CameraToClipSpace;
    float4x4 ClipSpaceToCamera;
    float4x4 WorldToCamera;
    float4x4 CameraToWorld;
    float4x4 WorldToClipSpace;
    float4x4 ClipSpaceToWorld;
    float4x4 ObjectToWorld;
    float4x4 WorldToObject;
    float4x4 ObjectToCamera;
    float4x4 ObjectToClipSpace;
};

cbuffer FogParams : register(b3)
{
    float4 FogColor;
    float FogDistance;
    float FogBias;
}

cbuffer RequestedResolution : register(b4)
{
    float TargetWidth;
    float TargetHeight;
}

struct psInput
{
    float4 position : SV_POSITION;
    float4 color : COLOR;
    float2 uv : uv;
    float fog : FOG;
    int id : ID;
    float attributeValue : VALUE;
};

sampler ClampedSampler : register(s0);

StructuredBuffer<Point> DataPoints : t0;
StructuredBuffer<Point> OverridePositionPoints : t1;

static const float NanPlaceholder = 999666.666;
static const int NotAnInt = -2147483647; // almost max neg

static const float Pow10[] = {
    1, 10, 100, 1000, 10000, 100000, 1000000};

psInput vsMain(uint id : SV_VertexID)
{
    uint overridePositionCount, stride;
    OverridePositionPoints.GetDimensions(overridePositionCount, stride);

    psInput output;

    int quadIndex = id % 6;
    int drawIndex = id / 6;

    float3 quadPos = Corners[quadIndex];

    int2 targetSize = int2(TargetWidth, TargetHeight);
    int2 evenTargetSize = targetSize & 0xfffffffe;
    int2 oddBit = targetSize & 1;

    float3 pos = overridePositionCount > 0 ? OverridePositionPoints[drawIndex].Position
                                           : DataPoints[drawIndex + StartIndex].Position;

    float4 posInObject = float4(pos, 1);
    float4 posInClipSpace = mul(posInObject, ObjectToClipSpace);

    float width = 4.0 * DigitCount;
    float height = 7;

    float2 s = float2(width / targetSize.x, 7.0 / targetSize.y) * Scale;
    int2 intLabelOffset = (int2)(LabelOffset.x + 30.0, LabelOffset.y - 10);

    posInClipSpace.xyz /= posInClipSpace.w;
    posInClipSpace.w = 1;

    // posInClipSpace.xy = floor((posInClipSpace.xy + 1) * 0.5 * (float2)targetSize - oddBit) / (float2)evenTargetSize * 2 - 1;

    float2 screenPos = ((posInClipSpace.xy + 1) * 0.5) * (float2)targetSize;
    screenPos = floor(screenPos) + 0.5; // pixel center
    posInClipSpace.xy = (screenPos / (float2)targetSize) * 2 - 1;

    posInClipSpace.xy += quadPos.xy * s + float2(LabelOffset.x / (float)TargetWidth, LabelOffset.y / (float)TargetHeight);
    output.position = posInClipSpace;

    output.uv = (float2(0, 1) + (quadPos.xy * 0.5 + 0.5) * float2(1, -1)); //* float2(DigitCount,1);

    if (ShowMode == 0)
    {
        output.id = drawIndex;
        output.attributeValue = NanPlaceholder;
    }
    else
    {
        output.id = NotAnInt;
        Point p = DataPoints[drawIndex + StartIndex];
        float f = 0;
        switch (ShowMode)
        {
        case 1:
            f = p.FX1;
            break;
        case 2:
            f = p.FX2;
            break;
        case 3:
            f = p.Position.x;
            break;
        case 4:
            f = p.Position.y;
            break;
        case 5:
            f = p.Position.z;
            break;
        case 6:
            f = p.Scale.x;
            break;
        case 7:
            f = p.Scale.y;
            break;
        case 8:
            f = p.Scale.z;
            break;
        case 9:
            f = p.Color.r;
            break;
        case 10:
            f = p.Color.g;
            break;
        case 11:
            f = p.Color.b;
            break;
        case 12:
            f = p.Color.a;
            break;
        }

        output.attributeValue = isnan(f) ? NanPlaceholder : f;
    }

    return output;
}

float4 psMain(psInput input) : SV_TARGET
{
    float2 uv = input.uv;

    int2 pixelPos = (uv * LabelSize) - 0.5 / LabelSize;
    // return float4(pixelPos * 1.0 / LabelSize, 0, 1);

    int2 p1 = pixelPos - LabelPadding;
    int2 digitIndex = p1 / PaddedDigitSize;
    int2 pInDigit = p1 - digitIndex * PaddedDigitSize;

    if (pInDigit.x > 2)
        discard;

    if (pInDigit.y > 4 || pInDigit.y < 0)
        discard;

    int2 decimalIndex = DigitCount - digitIndex - 1;

    // Draw int index
    int intValue = input.id + StartIndex;
    if (intValue != NotAnInt)
    {
        // Integer...
        int requiredDigitCount = intValue == 0 ? 0 : log10(intValue);
        if (requiredDigitCount < decimalIndex.x)
            discard;

        int digit = int((intValue / pow(10, decimalIndex.x))) % 10;
        bool bit = digits[pInDigit.y] >> ((10 - digit) * 3 - pInDigit.x - 1) & 1;
        if (!bit)
            discard;

        return Color;
    }
    // Attempt to draw float value
    else
    {
        float floatValue = input.attributeValue;
        if (floatValue == NanPlaceholder)
        {
            if (decimalIndex.x == 0)
            {
                bool bit = characters[pInDigit.y] >> ((10 - NanCharIndex) * 3 - pInDigit.x - 1) & 1;
                if (bit)
                    return Color;
            }
            discard;
        }

        bool isNegative = floatValue < -0.0001;

        if (isNegative)
        {
            floatValue *= -1;
        }

        const int decimalPosition = 3;

        // Add gap for decimal point
        if (decimalIndex.x == decimalPosition)
        {
            bool bit = characters[pInDigit.y] >> ((10 - 3) * 3 - pInDigit.x - 1) & 1;
            if (bit)
                return Color;

            discard;
        }

        decimalIndex += (decimalIndex <= decimalPosition - 1) ? 1 : 0;

        int requiredDigitCount = floatValue == 0 ? 1 : (log10(floatValue) + decimalPosition);
        bool needsLeadingZero = floatValue < 1;

        if (requiredDigitCount < decimalIndex.x - 1 - (needsLeadingZero ? 1 : 0))
        {
            if (isNegative && requiredDigitCount == decimalIndex.x - 2 - ((needsLeadingZero ? 1 : 0)))
            {
                bool bit = characters[pInDigit.y] >> ((10 - MinusCharIndex) * 3 - pInDigit.x - 1) & 1;
                if (bit)
                    return Color;
            }
            discard;
        }

        // double scaled = round(floatValue * pow(10, decimalPosition));
        // int digit = int(scaled / pow(10, decimalPosition - decimalIndex.x)) % 10;

        int digit = int(((floatValue + 0.0001) / pow(10, decimalIndex.x - decimalPosition - 1))) % 10;

        // float scale = Pow10[clamp(decimalIndex.x - decimalPosition - 1, 0, 7)];
        // int digit = int((round(floatValue * Pow10[decimalPosition]) / scale)) % 10;

        bool bit = digits[pInDigit.y] >> ((10 - digit) * 3 - pInDigit.x - 1) & 1;
        if (!bit)
            discard;

        return Color;

        return 1;
    }
}
