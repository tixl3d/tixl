// The centerline of [DrawTubes], shared by its vertex shader and its ring frame pre-pass so both orient the same
// curve. The including shader declares Points.

/** The ring frame pre-pass's result for one point. */
struct RingFrame
{
    /** xyz: unit direction of the ring's local Y axis, perpendicular to the line; w: distance from the line's start. */
    float4 NormalAndDistance;

    /** x: length of the line this point belongs to; y: distance from the buffer's first point, counting only the
        segments within lines; z: total length of all lines in the buffer. */
    float4 Line;
};

/** True if index i is inside the buffer and not a separator. */
bool IsLinePoint(int i, uint pointCount)
{
    if (i < 0 || i >= (int)pointCount)
        return false;

    return !IsSeparator(Points[i]);
}

float3 SafeNormalize(float3 v, float3 fallback)
{
    float lengthSquared = dot(v, v);
    return lengthSquared > 1e-12 ? v * rsqrt(lengthSquared) : fallback;
}

float3 AnyPerpendicular(float3 direction)
{
    return normalize(cross(direction, abs(direction.y) < 0.9 ? float3(0, 1, 0) : float3(1, 0, 0)));
}

/** Duplicate points beyond this many in a row count as a line end. */
static const int MaxCoincidentPoints = 8;

/** Unit direction from point i to the nearest point in the given direction (+1 or -1) that is not at the same
    position, or zero at a line end. Skipping duplicates gives coincident points the same ring, so the empty segment
    between them collapses instead of twisting into a fan. */
float3 DirectionToDistinctNeighbour(int i, int step, uint pointCount)
{
    float3 here = Points[i].Position;
    int neighbour = i;
    for (int skipped = 0; skipped < MaxCoincidentPoints; skipped++)
    {
        neighbour += step;
        if (!IsLinePoint(neighbour, pointCount))
            break;

        // A literal out-of-range index is invalid SPIR-V even in a dead branch, so it is clamped.
        float3 offset = Points[clamp(neighbour, 0, (int)pointCount - 1)].Position - here;
        if (dot(offset, offset) > 1e-12)
            return normalize(offset) * step;
    }

    return 0;
}

/** Unit direction of the line at a point. At inner points it bisects the angle between both segments, so a ring
    there meets both at the same angle, whatever their lengths. One-sided at line ends and next to separators. */
float3 CenterlineTangent(int i, uint pointCount)
{
    float3 incoming = DirectionToDistinctNeighbour(i, -1, pointCount);
    float3 outgoing = DirectionToDistinctNeighbour(i, 1, pointCount);

    // A full hairpin cancels both directions out; the outgoing one keeps the ring upright there.
    return SafeNormalize(incoming + outgoing, SafeNormalize(outgoing, SafeNormalize(incoming, float3(1, 0, 0))));
}
