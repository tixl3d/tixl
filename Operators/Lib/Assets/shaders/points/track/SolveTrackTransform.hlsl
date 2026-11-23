// Solves the 2D similarity transform (translation, rotation, uniform scale) that moves the tracked markers of
// a frame onto their anchors, and writes it as a single point: Position = translation, Rotation = rotation
// around Z, Scale = uniform scale, FX1 = number of markers used.
//
// Anchors are the markers' positions on the reference frame, placed by the reference transform. Lock mode
// outputs the transform itself, which pins the features to their anchors. Smooth mode keeps the camera path
// smoothed over a window of frames and removes only the jitter around it.
//
// Similarity transforms are written as complex numbers: T(z) = c * z + t with c = scale * e^(i * angle).

#include "shared/point.hlsl"

cbuffer Params : register(b0)
{
    float2 ReferencePosition;
    float ReferenceAngle;
    float ReferenceScale;

    float2 FocusPoint;
    float FocusRadius;
    int RejectOutliers;

    int MarkerCount;
    int SlotCount;
    int CurrentSlot;
    int ReferenceSlot;

    int Mode;
    int SmoothFrames;
    int AllowRotation;
    int AllowScale;
}

#define MODE_LOCK 0
#define MODE_SMOOTH 1

#define GROUP_SIZE 64
#define MAX_SOLVE_MARKERS 64
#define OUTLIER_ITERATIONS 3

// Two markers determine rotation and scale exactly, so parallax between them reads as a spin or a zoom.
// Below this count only translation is solved.
#define MIN_MARKERS_FOR_ROTATION_AND_SCALE 3

StructuredBuffer<Point> TrackPoints : register(t0);
RWStructuredBuffer<Point> Result : register(u0);

struct Similarity
{
    float2 C; // scale * (cos, sin)
    float2 T;
};

float2 ComplexMul(float2 a, float2 b) { return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x); }
float2 ComplexDiv(float2 a, float2 b) { return float2(a.x * b.x + a.y * b.y, a.y * b.x - a.x * b.y) / dot(b, b); }

float2 Apply(Similarity s, float2 z) { return ComplexMul(s.C, z) + s.T; }

Similarity Compose(Similarity outer, Similarity inner)
{
    Similarity r;
    r.C = ComplexMul(outer.C, inner.C);
    r.T = ComplexMul(outer.C, inner.T) + outer.T;
    return r;
}

Similarity Inverse(Similarity s)
{
    Similarity r;
    r.C = ComplexDiv(float2(1, 0), s.C);
    r.T = -ComplexMul(r.C, s.T);
    return r;
}

Similarity FromScaleAngle(float scale, float angle, float2 translation)
{
    Similarity s;
    s.C = scale * float2(cos(angle), sin(angle));
    s.T = translation;
    return s;
}

Similarity ReferenceTransform() { return FromScaleAngle(ReferenceScale, ReferenceAngle, ReferencePosition); }

// Weighted least-squares similarity from points to anchors. Returns false with fewer than one weighted point.
bool FitSimilarity(float2 points[MAX_SOLVE_MARKERS], float2 anchors[MAX_SOLVE_MARKERS], float weights[MAX_SOLVE_MARKERS],
                   int count, out Similarity result)
{
    result.C = float2(1, 0);
    result.T = 0;

    float weightSum = 0;
    float2 pointCenter = 0;
    float2 anchorCenter = 0;
    int weightedCount = 0;
    for (int i = 0; i < count; i++)
    {
        weightSum += weights[i];
        weightedCount += weights[i] > 0.01 ? 1 : 0;
        pointCenter += weights[i] * points[i];
        anchorCenter += weights[i] * anchors[i];
    }

    if (weightSum <= 1e-6)
        return false;

    pointCenter /= weightSum;
    anchorCenter /= weightSum;

    // Σ w * conj(p) * a and Σ w * |p|² over centered coordinates give the best c = scale * e^(i * angle).
    float2 cross = 0;
    float spread = 0;
    for (int j = 0; j < count; j++)
    {
        float2 p = points[j] - pointCenter;
        float2 a = anchors[j] - anchorCenter;
        cross += weights[j] * ComplexMul(float2(p.x, -p.y), a);
        spread += weights[j] * dot(p, p);
    }

    float2 c = float2(1, 0);
    if (spread > 1e-10 && weightedCount >= MIN_MARKERS_FOR_ROTATION_AND_SCALE)
    {
        if (AllowRotation != 0 && AllowScale != 0)
        {
            c = cross / spread;
        }
        else if (AllowRotation != 0)
        {
            c = length(cross) > 1e-10 ? normalize(cross) : float2(1, 0);
        }
        else if (AllowScale != 0)
        {
            c = float2(max(cross.x / spread, 1e-3), 0);
        }
    }

    result.C = c;
    result.T = anchorCenter - ComplexMul(c, pointCenter);
    return true;
}

float Median(float values[MAX_SOLVE_MARKERS], int count)
{
    // Selection by counting: count is small, and this keeps the array untouched.
    int middle = count / 2;
    for (int i = 0; i < count; i++)
    {
        int smaller = 0;
        int equal = 0;
        for (int j = 0; j < count; j++)
        {
            smaller += values[j] < values[i] ? 1 : 0;
            equal += values[j] == values[i] ? 1 : 0;
        }

        if (smaller <= middle && middle < smaller + equal)
            return values[i];
    }

    return values[0];
}

// Fits the frame in the given slot. usedCount is the number of markers with a usable point and anchor.
bool SolveSlot(int slot, out Similarity result, out int usedCount)
{
    float2 points[MAX_SOLVE_MARKERS];
    float2 anchors[MAX_SOLVE_MARKERS];
    float baseWeights[MAX_SOLVE_MARKERS];
    float weights[MAX_SOLVE_MARKERS];
    float residuals[MAX_SOLVE_MARKERS];

    Similarity reference = ReferenceTransform();
    int markerLimit = min(MarkerCount, MAX_SOLVE_MARKERS);
    int count = 0;
    for (int marker = 0; marker < markerLimit; marker++)
    {
        Point current = TrackPoints[slot * MarkerCount + marker];
        Point atReference = TrackPoints[ReferenceSlot * MarkerCount + marker];
        if (IsSeparator(current) || IsSeparator(atReference))
            continue;

        float weight = saturate(current.FX1);
        if (FocusRadius > 0)
        {
            float distance = length(current.Position.xy - FocusPoint) / FocusRadius;
            weight *= exp(-distance * distance);
        }

        points[count] = current.Position.xy;
        anchors[count] = Apply(reference, atReference.Position.xy);
        baseWeights[count] = weight;
        weights[count] = weight;
        count++;
    }

    usedCount = count;
    if (!FitSimilarity(points, anchors, weights, count, result))
        return false;

    if (RejectOutliers == 0 || count < 3)
        return true;

    // Iteratively reweighted least squares with Tukey's biweight: markers far off the consensus (another depth,
    // a moving object, a slipped track) fade to zero weight. The cutoff follows the median residual.
    for (int iteration = 0; iteration < OUTLIER_ITERATIONS; iteration++)
    {
        for (int i = 0; i < count; i++)
            residuals[i] = length(Apply(result, points[i]) - anchors[i]);

        float cutoff = max(4.685 * 1.4826 * Median(residuals, count), 1e-3);
        for (int k = 0; k < count; k++)
        {
            float u = residuals[k] / cutoff;
            float tukey = u < 1 ? (1 - u * u) * (1 - u * u) : 0;
            weights[k] = baseWeights[k] * tukey;
        }

        Similarity refined;
        if (!FitSimilarity(points, anchors, weights, count, refined))
            break;

        result = refined;
    }

    return true;
}

groupshared float4 WindowTransforms[GROUP_SIZE]; // log scale, angle, translation
groupshared float WindowWeights[GROUP_SIZE];

[numthreads(GROUP_SIZE, 1, 1)]
void main(uint threadIndex : SV_GroupIndex)
{
    int halfWindow = Mode == MODE_SMOOTH ? clamp(SmoothFrames, 1, (GROUP_SIZE - 1) / 2) : 0;
    int offset = (int)threadIndex - halfWindow;
    int slot = CurrentSlot + offset;

    WindowWeights[threadIndex] = 0;
    WindowTransforms[threadIndex] = 0;

    Similarity solved;
    int usedCount = 0;
    bool isSolved = false;
    if (abs(offset) <= halfWindow && slot >= 0 && slot < SlotCount)
        isSolved = SolveSlot(slot, solved, usedCount);

    if (isSolved)
    {
        float sigma = max(halfWindow * 0.5, 1);
        WindowWeights[threadIndex] = exp(-0.5 * (offset * offset) / (sigma * sigma));
        WindowTransforms[threadIndex] = float4(log(max(length(solved.C), 1e-6)), atan2(solved.C.y, solved.C.x), solved.T);
    }

    GroupMemoryBarrierWithGroupSync();

    if (offset != 0)
        return;

    // Unsolvable frame: keep the previous transform so the image holds still, and report no markers.
    if (!isSolved)
    {
        Result[0].FX1 = 0;
        return;
    }

    Similarity output = solved;
    if (Mode == MODE_SMOOTH)
    {
        float currentAngle = WindowTransforms[threadIndex].y;
        float weightSum = 0;
        float4 sum = 0;
        for (int i = 0; i < 2 * halfWindow + 1; i++)
        {
            float w = WindowWeights[i];
            float4 t = WindowTransforms[i];

            // Average angles relative to the current one so a wrap at ±π doesn't flip the mean.
            float angleDelta = t.y - currentAngle;
            t.y = currentAngle + atan2(sin(angleDelta), cos(angleDelta));
            sum += w * t;
            weightSum += w;
        }

        float4 mean = sum / weightSum;
        Similarity smoothed = FromScaleAngle(exp(mean.x), mean.y, mean.zw);

        // Show the video where the smoothed camera path puts it, placed by the reference transform.
        output = Compose(ReferenceTransform(), Compose(Inverse(smoothed), solved));
    }

    float angle = atan2(output.C.y, output.C.x);
    float scale = length(output.C);

    Point p;
    p.Position = float3(output.T, 0);
    p.FX1 = usedCount;
    p.Rotation = float4(0, 0, sin(angle * 0.5), cos(angle * 0.5));
    p.Color = 1;
    p.Scale = scale;
    p.FX2 = 0;
    Result[0] = p;
}
