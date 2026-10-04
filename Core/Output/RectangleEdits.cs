namespace T3.Core.Output;

/// <summary>What a change to a surface's rectangle means for everything aimed at that wall.</summary>
public enum RectangleEdits
{
    /// <summary>The rectangle was reshaped on the wall — a crop. The corner pins and the traced photo are
    /// re-derived for the new corners, so the wall keeps its pixels and the photo keeps its features.</summary>
    OnTheWall,

    /// <summary>Only how big the rectangle really is was declared — a re-metering. Nothing aimed at the wall
    /// moves; what is stored in metres scales instead, which is the caller's to do.</summary>
    Declared,
}
