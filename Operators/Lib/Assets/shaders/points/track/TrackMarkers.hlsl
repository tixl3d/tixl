// Sparse feature tracking for [TrackMarkers].
//
// Positions are in video space: origin at the image center, y up, y spans -1..1 over the image height.
// Tracking runs in pixels of a luma pyramid. Each marker keeps two template sets (one patch per pyramid level):
// the key template from the seed frame and the recent template from the last tracked frame. Tracking is
// translation-only pyramidal Lucas-Kanade (inverse compositional) with brightness normalization, so passing
// shadows don't pull the patch. Confidence is the zero-mean NCC against the key template, which exposes drift.

#include "shared/point.hlsl"
#include "shared/quat-functions.hlsl"

cbuffer Params : register(b0)
{
    float2 ImageSize;
    float MinConfidence;
    float QualityScale;

    int MarkerCount;
    int PatchSize;
    int Levels;
    int TemplateLevels;

    int Mode;
    int SourceSlot;
    int PreviousSlot;
    int TargetSlot;

    int UseRecentTemplate;
    int IterationsPerLevel;
    int ClearCount;
    int FrameCount;

    int ForceCapture;
    int CheckRoundTrip;
    float MaxRoundTripError;
    int __padding;
}

#define MODE_CAPTURE 0
#define MODE_ADVANCE 1
#define MODE_HIDE 2

#define GROUP_SIZE 256
#define MAX_TEMPLATE_SIZE 33
#define MAX_PATCH_SIZE (MAX_TEMPLATE_SIZE - 2)

Texture2D<float4> Image : register(t0);
Texture2D<float> Luma : register(t1);
Texture2D<float> PreviousLuma : register(t3);
StructuredBuffer<Point> SeedPoints : register(t2);

RWStructuredBuffer<Point> TrackPoints : register(u0);
RWStructuredBuffer<Point> CurrentPoints : register(u1);
RWStructuredBuffer<Point> SeedQuality : register(u2);
RWStructuredBuffer<float> KeyTemplates : register(u3);
RWStructuredBuffer<float> RecentTemplates : register(u4);
RWTexture2D<float> LumaOut : register(u5);
RWStructuredBuffer<Point> CapturedSeeds : register(u6);

groupshared float TemplateValues[MAX_TEMPLATE_SIZE * MAX_TEMPLATE_SIZE];
groupshared float GradientX[MAX_PATCH_SIZE * MAX_PATCH_SIZE];
groupshared float GradientY[MAX_PATCH_SIZE * MAX_PATCH_SIZE];
groupshared float4 ReduceValues[GROUP_SIZE];

//--- Luma pyramid base -------------------------------------------------------------------

[numthreads(8, 8, 1)]
void luma(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)ImageSize.x || id.y >= (uint)ImageSize.y)
        return;

    float3 rgb = Image.Load(int3(id.xy, 0)).rgb;
    LumaOut[id.xy] = dot(rgb, float3(0.2126, 0.7152, 0.0722));
}

//--- Track buffer reset ------------------------------------------------------------------

Point SeparatorPoint()
{
    Point p;
    p.Position = 0;
    p.FX1 = 0;
    p.Rotation = float4(0, 0, 0, 1);
    p.Color = 1;
    p.Scale = NAN;
    p.FX2 = 0;
    return p;
}

[numthreads(64, 1, 1)]
void clear(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= (uint)ClearCount)
        return;

    TrackPoints[id.x] = SeparatorPoint();
}

//--- Helpers -----------------------------------------------------------------------------

float2 VideoToPixel(float2 videoPosition)
{
    float aspect = ImageSize.x / ImageSize.y;
    float2 uv = float2((videoPosition.x / aspect + 1) * 0.5, (1 - videoPosition.y) * 0.5);
    return uv * ImageSize;
}

float2 PixelToVideo(float2 pixel)
{
    float aspect = ImageSize.x / ImageSize.y;
    float2 uv = pixel / ImageSize;
    return float2((uv.x * 2 - 1) * aspect, 1 - uv.y * 2);
}

// Bilinear sample of a pyramid level; positions are continuous pixel coordinates of that level.
float SampleLuma(Texture2D<float> luma, float2 position, uint level)
{
    int2 size = max(int2(1, 1), int2(ImageSize) >> level);
    float2 p = position - 0.5;
    int2 i0 = (int2)floor(p);
    float2 f = p - i0;
    int2 a = clamp(i0, int2(0, 0), size - 1);
    int2 b = clamp(i0 + 1, int2(0, 0), size - 1);
    float v00 = luma.Load(int3(a.x, a.y, level));
    float v10 = luma.Load(int3(b.x, a.y, level));
    float v01 = luma.Load(int3(a.x, b.y, level));
    float v11 = luma.Load(int3(b.x, b.y, level));
    return lerp(lerp(v00, v10, f.x), lerp(v01, v11, f.x), f.y);
}

// Must be reached by every thread of the group.
float4 ReduceSum(float4 value, uint threadIndex)
{
    ReduceValues[threadIndex] = value;
    GroupMemoryBarrierWithGroupSync();

    [unroll]
    for (uint stride = GROUP_SIZE / 2; stride > 0; stride >>= 1)
    {
        if (threadIndex < stride)
            ReduceValues[threadIndex] += ReduceValues[threadIndex + stride];

        GroupMemoryBarrierWithGroupSync();
    }

    float4 sum = ReduceValues[0];
    GroupMemoryBarrierWithGroupSync();
    return sum;
}

int TemplateSize() { return PatchSize + 2; }

int TemplateOffset(uint marker, uint level)
{
    int templateSize = TemplateSize();
    return (int)(marker * TemplateLevels + level) * templateSize * templateSize;
}

// Template cell (i, j) sits at this offset from the patch center, in pixels of its level.
float2 TemplateCellOffset(int cellIndex)
{
    int templateSize = TemplateSize();
    int halfSize = (PatchSize - 1) / 2 + 1;
    return float2(cellIndex % templateSize - halfSize, cellIndex / templateSize - halfSize);
}

void CaptureTemplates(uint marker, float2 pixelPosition, bool alsoKey, uint threadIndex)
{
    int templateSize = TemplateSize();
    int cellCount = templateSize * templateSize;
    for (int level = 0; level < TemplateLevels; level++)
    {
        float2 center = pixelPosition / (float)(1 << level);
        int offset = TemplateOffset(marker, level);
        for (int cell = threadIndex; cell < cellCount; cell += GROUP_SIZE)
        {
            float value = SampleLuma(Luma, center + TemplateCellOffset(cell), level);
            RecentTemplates[offset + cell] = value;
            if (alsoKey)
                KeyTemplates[offset + cell] = value;
        }
    }
}

// Loads a template level into group memory and returns its gradients' statistics.
// Inner cells are the patch; the one-cell border only feeds the central differences.
void LoadTemplateLevel(uint marker, uint level, bool fromRecent, uint threadIndex)
{
    int templateSize = TemplateSize();
    int cellCount = templateSize * templateSize;
    int offset = TemplateOffset(marker, level);
    for (int cell = threadIndex; cell < cellCount; cell += GROUP_SIZE)
    {
        TemplateValues[cell] = fromRecent ? RecentTemplates[offset + cell] : KeyTemplates[offset + cell];
    }

    GroupMemoryBarrierWithGroupSync();

    int patchCellCount = PatchSize * PatchSize;
    for (int patchCell = threadIndex; patchCell < patchCellCount; patchCell += GROUP_SIZE)
    {
        int x = patchCell % PatchSize + 1;
        int y = patchCell / PatchSize + 1;
        int center = y * templateSize + x;
        GradientX[patchCell] = (TemplateValues[center + 1] - TemplateValues[center - 1]) * 0.5;
        GradientY[patchCell] = (TemplateValues[center + templateSize] - TemplateValues[center - templateSize]) * 0.5;
    }

    GroupMemoryBarrierWithGroupSync();
}

float PatchTemplateValue(int patchCell)
{
    int templateSize = TemplateSize();
    int x = patchCell % PatchSize + 1;
    int y = patchCell / PatchSize + 1;
    return TemplateValues[y * templateSize + x];
}

float2 PatchCellOffset(int patchCell)
{
    int halfSize = (PatchSize - 1) / 2;
    return float2(patchCell % PatchSize - halfSize, patchCell / PatchSize - halfSize);
}

Point TrackedPoint(float2 pixelPosition, float confidence, uint marker)
{
    int2 texel = clamp((int2)pixelPosition, int2(0, 0), int2(ImageSize) - 1);

    Point p;
    p.Position = float3(PixelToVideo(pixelPosition), 0);
    p.FX1 = confidence;
    p.Rotation = float4(0, 0, 0, 1);
    p.Color = float4(Image.Load(int3(texel, 0)).rgb, 1);
    p.Scale = 1;
    p.FX2 = marker;
    return p;
}

// Keeps where the marker was last searched and how well it matched, so a lost marker can be diagnosed.
Point LostPoint(uint marker, float2 pixelPosition, float confidence)
{
    Point p = SeparatorPoint();
    p.Position = float3(PixelToVideo(pixelPosition), 0);
    p.FX1 = confidence;
    p.FX2 = marker;
    return p;
}

bool IsPatchInsideImage(float2 pixelPosition)
{
    float extent = (PatchSize - 1) / 2 + 1;
    return all(pixelPosition >= extent) && all(pixelPosition <= ImageSize - extent);
}


//--- Seed quality --------------------------------------------------------------------------

// Shi-Tomasi: the smaller eigenvalue of the structure tensor is the gradient energy in the weakest direction.
// It is low on flat areas and on straight edges (where tracking can only follow across the edge).
float ComputeSeedQuality(uint marker, uint threadIndex)
{
    LoadTemplateLevel(marker, 0, false, threadIndex);

    float4 tensor = 0;
    int patchCellCount = PatchSize * PatchSize;
    for (int patchCell = threadIndex; patchCell < patchCellCount; patchCell += GROUP_SIZE)
    {
        float gx = GradientX[patchCell];
        float gy = GradientY[patchCell];
        tensor += float4(gx * gx, gx * gy, gy * gy, 0);
    }

    tensor = ReduceSum(tensor, threadIndex) / patchCellCount;
    float halfTrace = (tensor.x + tensor.z) * 0.5;
    float halfDifference = (tensor.x - tensor.z) * 0.5;
    float smallerEigenvalue = halfTrace - sqrt(halfDifference * halfDifference + tensor.y * tensor.y);
    return saturate(sqrt(max(smallerEigenvalue, 0)) * QualityScale);
}

//--- Tracking ------------------------------------------------------------------------------

// Searches luma for the marker's key or recent template, coarse to fine, starting at startPosition.
float2 TrackLucasKanade(Texture2D<float> luma, uint marker, bool fromRecent, float2 startPosition, uint threadIndex)
{
    float2 position = startPosition;
    int patchCellCount = PatchSize * PatchSize;

    for (int level = Levels - 1; level >= 0; level--)
    {
        LoadTemplateLevel(marker, level, fromRecent, threadIndex);

        float4 templateSums = 0;  // Σgx², Σgxgy, Σgy², ΣT
        float4 gradientSums = 0;  // Σgx, Σgy, ΣgxT, ΣgyT
        float templateSquares = 0;
        for (int patchCell = threadIndex; patchCell < patchCellCount; patchCell += GROUP_SIZE)
        {
            float gx = GradientX[patchCell];
            float gy = GradientY[patchCell];
            float t = PatchTemplateValue(patchCell);
            templateSums += float4(gx * gx, gx * gy, gy * gy, t);
            gradientSums += float4(gx, gy, gx * t, gy * t);
            templateSquares += t * t;
        }

        templateSums = ReduceSum(templateSums, threadIndex);
        gradientSums = ReduceSum(gradientSums, threadIndex);
        templateSquares = ReduceSum(float4(templateSquares, 0, 0, 0), threadIndex).x;

        float templateMean = templateSums.w / patchCellCount;
        float templateVariance = max(templateSquares / patchCellCount - templateMean * templateMean, 1e-8);

        // Σ∇T·(T - mean T): the template part of the error term, constant per level.
        float2 templateTerm = gradientSums.zw - templateMean * gradientSums.xy;

        // A flat patch has no usable gradient: a zero inverse keeps the current estimate on this level.
        float determinant = templateSums.x * templateSums.z - templateSums.y * templateSums.y;
        float inverseDeterminant = determinant > 1e-12 ? 1 / determinant : 0;
        float2x2 inverseHessian = float2x2(templateSums.z, -templateSums.y, -templateSums.y, templateSums.x)
                                  * inverseDeterminant;

        float levelScale = (float)(1 << level);
        for (int iteration = 0; iteration < IterationsPerLevel; iteration++)
        {
            float2 levelPosition = position / levelScale;

            float4 imageSums = 0; // ΣI, ΣI², ΣgxI, ΣgyI
            for (int cell = threadIndex; cell < patchCellCount; cell += GROUP_SIZE)
            {
                float i = SampleLuma(luma, levelPosition + PatchCellOffset(cell), level);
                imageSums += float4(i, i * i, GradientX[cell] * i, GradientY[cell] * i);
            }

            imageSums = ReduceSum(imageSums, threadIndex);

            float imageMean = imageSums.x / patchCellCount;
            float imageVariance = max(imageSums.y / patchCellCount - imageMean * imageMean, 1e-8);
            float gain = sqrt(templateVariance / imageVariance);

            // Σ∇T·e with e = gain·(I - mean I) - (T - mean T).
            float2 imageTerm = gain * (imageSums.zw - imageMean * gradientSums.xy);
            float2 step = mul(inverseHessian, imageTerm - templateTerm);

            // Inverse compositional update for a translation.
            float maxStep = (PatchSize - 1) * 0.5;
            step = clamp(step, -maxStep, maxStep);
            position -= step * levelScale;
        }
    }

    return position;
}

// Zero-mean normalized cross-correlation between the level 0 key template and the image at position.
float KeyTemplateCorrelation(uint marker, float2 position, uint threadIndex)
{
    int templateSize = TemplateSize();
    int offset = TemplateOffset(marker, 0);
    int patchCellCount = PatchSize * PatchSize;

    float4 sums = 0; // ΣK, ΣK², ΣI, ΣI²
    float crossSum = 0;
    for (int cell = threadIndex; cell < patchCellCount; cell += GROUP_SIZE)
    {
        int x = cell % PatchSize + 1;
        int y = cell / PatchSize + 1;
        float k = KeyTemplates[offset + y * templateSize + x];
        float i = SampleLuma(Luma, position + PatchCellOffset(cell), 0);
        sums += float4(k, k * k, i, i * i);
        crossSum += k * i;
    }

    sums = ReduceSum(sums, threadIndex);
    crossSum = ReduceSum(float4(crossSum, 0, 0, 0), threadIndex).x;

    float n = patchCellCount;
    float keyMean = sums.x / n;
    float imageMean = sums.z / n;
    float keyVariance = sums.y / n - keyMean * keyMean;
    float imageVariance = sums.w / n - imageMean * imageMean;
    float covariance = crossSum / n - keyMean * imageMean;
    return covariance / sqrt(max(keyVariance * imageVariance, 1e-12));
}

[numthreads(GROUP_SIZE, 1, 1)]
void track(uint3 groupId : SV_GroupID, uint threadIndex : SV_GroupIndex)
{
    // One group per marker, dispatched exactly MarkerCount times. Group barriers must not sit under
    // data-dependent branches, so every thread runs every reduction and only the writes are gated.
    uint marker = groupId.x;
    uint targetIndex = TargetSlot * MarkerCount + marker;

    if (Mode == MODE_HIDE)
    {
        if (threadIndex == 0)
            CurrentPoints[marker] = LostPoint(marker, ImageSize * 0.5, 0);

        return;
    }

    if (Mode == MODE_CAPTURE)
    {
        // Only seeds that moved are captured again, so editing one seed keeps every other marker's track.
        Point seed = SeedPoints[marker];
        Point captured = CapturedSeeds[marker];
        bool hasChanged = ForceCapture != 0
                          || IsSeparator(seed) != IsSeparator(captured)
                          || any(seed.Position.xy != captured.Position.xy);

        float2 pixelPosition = VideoToPixel(seed.Position.xy);
        bool isValid = !IsSeparator(seed) && IsPatchInsideImage(pixelPosition);

        CaptureTemplates(marker, pixelPosition, hasChanged, threadIndex);
        if (hasChanged)
        {
            for (int slot = threadIndex; slot < FrameCount; slot += GROUP_SIZE)
                TrackPoints[slot * MarkerCount + marker] = LostPoint(marker, pixelPosition, 0);
        }

        DeviceMemoryBarrierWithGroupSync();

        float quality = ComputeSeedQuality(marker, threadIndex) * (isValid ? 1 : 0);

        if (threadIndex == 0)
        {
            Point qualityPoint = TrackedPoint(pixelPosition, quality, marker);
            qualityPoint.Color = float4(lerp(float3(1, 0.2, 0.1), float3(0.2, 1, 0.3), quality), 1);
            if (IsSeparator(seed))
                qualityPoint.Scale = NAN;

            SeedQuality[marker] = qualityPoint;

            Point result = isValid ? TrackedPoint(pixelPosition, 1, marker) : LostPoint(marker, pixelPosition, 0);
            TrackPoints[targetIndex] = result;
            CurrentPoints[marker] = result;
            CapturedSeeds[marker] = seed;
        }

        return;
    }

    // MODE_ADVANCE: keep the marker if this frame already has it, otherwise track it from the source frame.
    // Frames seen before are advanced too, so a re-seeded marker gets filled in without touching the others.
    Point target = TrackPoints[targetIndex];
    bool hasTarget = !IsSeparator(target);

    Point source = TrackPoints[max(SourceSlot, 0) * MarkerCount + marker];
    bool hasSource = SourceSlot >= 0 && !IsSeparator(source);
    float2 sourcePosition = hasSource ? VideoToPixel(source.Position.xy) : ImageSize * 0.5;

    float2 predicted = sourcePosition;
    if (PreviousSlot >= 0)
    {
        Point previous = TrackPoints[PreviousSlot * MarkerCount + marker];
        if (!IsSeparator(previous))
            predicted += sourcePosition - VideoToPixel(previous.Position.xy);
    }

    // Runs for kept markers as well, because the reductions inside must be reached by the whole group.
    float2 tracked = TrackLucasKanade(Luma, marker, UseRecentTemplate != 0, predicted, threadIndex);
    bool isInside = IsPatchInsideImage(tracked);
    float confidence = KeyTemplateCorrelation(marker, tracked, threadIndex);

    // All reads of the recent template are done; replace it with this frame's patch. It is the template for
    // the next frame and for the round trip below.
    DeviceMemoryBarrierWithGroupSync();
    CaptureTemplates(marker, hasTarget ? VideoToPixel(target.Position.xy) : tracked, false, threadIndex);
    DeviceMemoryBarrierWithGroupSync();

    // Forward-backward check: tracking this frame's patch back into the source frame must land where the
    // marker started. Occlusion and slipping on repeated texture break that.
    float roundTripError = 0;
    if (CheckRoundTrip != 0)
    {
        float2 returned = TrackLucasKanade(PreviousLuma, marker, true, tracked, threadIndex);
        roundTripError = length(returned - sourcePosition);
    }

    bool isLost = !hasSource || !isInside || confidence < MinConfidence || roundTripError > MaxRoundTripError;

    if (threadIndex == 0)
    {
        // Without a source the search started nowhere: carry over where the marker was lost, or keep this
        // frame's own record when no source frame was given.
        Point lostPoint = hasSource ? LostPoint(marker, tracked, confidence)
                        : SourceSlot >= 0 ? source
                        : target;
        lostPoint.FX2 = marker;
        lostPoint.Scale = NAN;
        Point result = hasTarget ? target
                       : isLost ? lostPoint
                       : TrackedPoint(tracked, saturate(confidence), marker);
        if (!hasTarget)
            TrackPoints[targetIndex] = result;

        CurrentPoints[marker] = result;
    }
}
