cbuffer ParamConstants : register(b0)
{
    float2 Center;
    float Width;
    float Rotation;
    float2 Range;
    float ShowOriginal;
}

cbuffer TimeConstants : register(b1)
{
    float globalTime;
    float time;
    float runTime;
    float beatTime;
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

Texture2D<float4> inputTexture : register(t0);
sampler texSampler : register(s0);

static float lineThickness;

float IsBetween(float value, float low, float high)
{
    return (value >= low && value <= high) ? 1 : 0;
}

float SubdivisionLine(float n, float r)
{
    float t = lineThickness * (Range.y - Range.x);

    float f = 1 - saturate(t * r * 3);

    float x = n % ((1 + 1. / 255) / r) < t;
    return x * f;

    // return (n + lineThickness, n, colorOnLine.rgba) * smoothstep(n - lineThickness, n, colorOnLine.rgba)
}

float4 psMain(vsOutput psInput) : SV_TARGET
{
    uint width, height;
    inputTexture.GetDimensions(width, height);

    float2 uv = psInput.texCoord;
    float4 orgColor = inputTexture.Sample(texSampler, uv);

    float aspectRation = TargetWidth / TargetHeight;
    float2 p = uv;
    p -= 0.5;
    p.x *= aspectRation;

    float radians = Rotation / 180 * 3.141578;
    float2 angle = float2(sin(radians), cos(radians));
    float distanceFromCenter = dot(p - Center * float2(1, -1), angle);
    float normalizedDistance = -distanceFromCenter / Width;
    float4 visibleOrgColor = lerp(float4(0, 0, 0, 0), orgColor, ShowOriginal);

    lineThickness = 1.1 / (height) / Width;
    float nInRange = (normalizedDistance) * (Range.y - Range.x) + Range.x;
    float4 subdivisionLines = (SubdivisionLine(nInRange, 8) * float4(0.0, 0, 0, .3) + SubdivisionLine(nInRange, 1) * float4(0.0, 0, 0, 1) + SubdivisionLine(nInRange, 256) * float4(0.0, 0, 0, 0.3)) * (normalizedDistance < 1);

    // Bottom Line
    // if (IsBetween(normalizedDistance, 1, 1 + 0.01))
    // {
    //     return float4(0., 0., 0., 1);
    // }
    float2 pixelposition = uv * float2(width, height);

    float2 pOnLine = p;
    pOnLine += (-distanceFromCenter) * angle;
    pOnLine.x /= aspectRation;
    pOnLine += 0.5;
    float4 colorOnLine = inputTexture.Sample(texSampler, pOnLine);
    colorOnLine = (colorOnLine - +Range.x) / (Range.y - Range.x);

    // Curves...
    float4 curveColor = 0;
    float4 curveShape2 = smoothstep(normalizedDistance, normalizedDistance + lineThickness, colorOnLine.rgba);

    float channelAlpha = 0.1;
    float n = -0.2;
    float4 curveShape = curveShape2.r * float4(1, n, n, channelAlpha)   //
                        + curveShape2.g * float4(n, 1, n, channelAlpha) //
                        + curveShape2.b * float4(n, n, 1, channelAlpha) //
                        + curveShape2.a * float4(1, 1, 1, channelAlpha);

    float4 curveLines = smoothstep(normalizedDistance + lineThickness * float4(1, 1, 1, 1.5), normalizedDistance, colorOnLine.rgba)   //
                        * smoothstep(normalizedDistance - lineThickness * float4(1, 1, 1, 1.5), normalizedDistance, colorOnLine.rgba) //
                        * float4(2, 2, 2, 1.5 * ((pixelposition.x + pixelposition.y) % 10 > 5 ? 1 : 0));

    curveLines.a += length(curveLines.rgb) * 0.3;
    if (normalizedDistance < 0)
    {
        curveShape = 0;
    }

    curveColor.rgba = curveLines + curveShape * 0.6 + subdivisionLines;
    // return curveColor;

    if (normalizedDistance < 0)
        curveColor *= 0.1;

    // Zebra pattern for highlight clamping
    float3 clamping = (colorOnLine.rgb > 1 || colorOnLine.rgb < 0) ? float3(1, 1, 1) : float3(0, 0, 0);

    float pattern = (pixelposition.x + pixelposition.y + 0.5 + beatTime * 100) % 8 < 2 ? 1 : -1;

    float3 clampedAreaRGB = clamping * curveShape.rgb * ((normalizedDistance > 1 || normalizedDistance < 0) ? 1 : 0);
    float4 clampedArea = float4(clampedAreaRGB, length(clampedAreaRGB) * pattern * 0.5);
    float heighlightExcessiveAlpha = ((normalizedDistance > 1 || normalizedDistance < 0) && colorOnLine.a > normalizedDistance) ? 1 : 0;

    bool isBetweenCurveRange = normalizedDistance >= 0 && normalizedDistance <= 1;

    return curveColor + clampedArea + visibleOrgColor * (isBetweenCurveRange ? 0.2 : 1);
}
