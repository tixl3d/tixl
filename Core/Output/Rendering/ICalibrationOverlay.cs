#nullable enable
using System;
using System.Collections.Generic;
using T3.Graphics.Compat;
using T3.Core.DataTypes.Vector;

namespace T3.Core.Output.Rendering;

/// <summary>
/// What an authoring host adds to a composite while a surface is being calibrated: the wall's own photo
/// projected around each reference point, the measuring lines, the alignment marks. None of it belongs to a
/// show, so a player installs nothing and <see cref="OutputCompositor"/> composites without it.
/// </summary>
public interface ICalibrationOverlay
{
    /// <summary>Starts a frame's collection, before any surface is walked.</summary>
    void BeginCollect();

    /// <summary>Whether this surface currently projects its calibration photo, which the composite must make room for.</summary>
    bool ProjectsPhoto(Guid surfaceId);

    /// <summary>Holds back this surface's photo discs until the canvas size is known.</summary>
    void DeferPhotoFragments(Surface surface, Surface.OutputMapping mapping, Matrix4x4 homography);

    /// <summary>Appends the held-back photo discs to the frame's draw items.</summary>
    void CollectPhotoFragments(Int2 canvasResolution, List<OutputCompositor.DrawItem> drawItems);

    /// <summary>Collects this surface's annotations, which are drawn over the composite.</summary>
    void CollectAnnotations(Surface surface, Surface.OutputMapping mapping, Vector2 canvasSize);

    /// <summary>Draws what was collected, once the composite itself is in the target.</summary>
    void Draw(DeviceContext deviceContext, Int2 canvasResolution);
}
