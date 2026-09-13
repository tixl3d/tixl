namespace T3.Core.Output;

/// <summary>How a patch's rectangle follows its output canvas when the canvas changes size or aspect.</summary>
public enum PatchScaleModes
{
    /// <summary>The quad is stored as a fraction of the canvas, so a canvas of another aspect stretches it along.</summary>
    Stretch,

    /// <summary>The quad is derived: the largest rectangle of the patch's aspect ratio that fits the canvas, scaled
    /// and centred.</summary>
    Fit,
}
