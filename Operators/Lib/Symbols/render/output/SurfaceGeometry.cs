#nullable enable
using T3.Core.DataTypes;
using T3.Core.Output;

namespace Lib.render.output;

/// <summary>
/// One output-setup surface or region as a quad in the stage, at its real size and place: the frame a projector
/// camera is solved in, so content drawn on it through [UseProjectorCam] lands on the real wall. TexCoord runs 0..1
/// over it, the top at the surface's top, which is how [SurfaceReferenceImage] lays out the straightened photo.
/// </summary>
[Guid("351c58d2-bb46-415d-871c-ae1c3db97444")]
internal sealed class SurfaceGeometry : Instance<SurfaceGeometry>, IStatusProvider
{
    [Output(Guid = "5a5d850a-d301-451f-acda-a1e880cdf48d")]
    public readonly Slot<MeshGeometry?> Geometry = new();

    public SurfaceGeometry()
    {
        Geometry.UpdateAction = Update;

        // Re-derived only when the setup changes, so the mesh compiled downstream stays cached between edits.
        ActiveSetup.Changed += OnSetupChanged;
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            ActiveSetup.Changed -= OnSetupChanged;

        base.Dispose(isDisposing);
    }

    private void OnSetupChanged() => Geometry.DirtyFlag.ForceInvalidate();

    private void Update(EvaluationContext context)
    {
        var setup = ActiveSetup.Current;
        var surfaceId = Surface.GetValue(context);
        var surround = Math.Clamp(Surround.GetValue(context), 0f, 2f);
        if (setup == null || setup.FindSurface(surfaceId) is not { } surface)
        {
            Geometry.Value = null;
            SetWarning(surfaceId == Guid.Empty ? NoSurfaceWarning : MissingSurfaceWarning, null);
            return;
        }

        T3.Core.Output.SurfaceGeometry.GetStageRect(setup, surface, out var world, out var min, out var max, out var lift);

        // Grown by the same share of its size as a [SurfaceReferenceImage] with that Surround, so the photo still fits.
        var margin = (max - min) * surround;
        min -= margin;
        max += margin;

        var normal = Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, world));
        var normals = _geometry.Attributes.GetOrCreate<Vector3>(GeometryAttributeNames.Normal, AttributeDomain.Corner, 4);
        var texCoords = _geometry.Attributes.GetOrCreate<Vector2>(GeometryAttributeNames.TexCoord, AttributeDomain.Corner, 4);

        // Counter-clockwise seen from the front: bottom-left, bottom-right, top-right, top-left.
        for (var corner = 0; corner < 4; corner++)
        {
            var inRect = _cornerFractions[corner];
            var inSurface = min + (max - min) * inRect;
            _geometry.Positions[corner] = Vector3.Transform(new Vector3(inSurface.X, inSurface.Y, lift), world);
            _geometry.CornerPointIndices[corner] = corner;
            normals.Values[corner] = normal;
            texCoords.Values[corner] = inRect;
        }

        var centre = (_geometry.Positions[0] + _geometry.Positions[2]) * 0.5f;
        _geometry.Parts[0] = new GeometryPart(0, 1, centre, 0, 0);
        _geometry.InvalidateTopologyCaches();
        Geometry.Value = _geometry;

        SetWarning(IsOffStage(setup, surface) ? OffStageWarning : null, surface.Name);
    }

    /// <summary>
    /// Whether the surface's root has no place in the stage while a projector camera was solved from another surface:
    /// it is then drawn at that surface's origin rather than where it is in the room.
    /// </summary>
    private static bool IsOffStage(Setup setup, Surface surface)
    {
        var root = surface;
        for (var guard = 0; root.ParentId != Guid.Empty && guard < 16; guard++)
        {
            if (setup.FindSurface(root.ParentId) is not { } parent)
                break;

            root = parent;
        }

        if (root.Placement != null)
            return false;

        foreach (var output in setup.Outputs)
        {
            if (output.Camera is { Pose: not null } camera && camera.SolvedFromSurfaceId != Guid.Empty && camera.SolvedFromSurfaceId != root.Id)
                return true;
        }

        return false;
    }

    /** Rebuilt only when what it reports changes, so a steady setup costs no string per frame. */
    private void SetWarning(string? format, string? surfaceName)
    {
        if (ReferenceEquals(format, _warningFormat) && ReferenceEquals(surfaceName, _warningSurfaceName))
            return;

        _warningFormat = format;
        _warningSurfaceName = surfaceName;
        _warning = format == null ? null : string.Format(format, surfaceName);
    }

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        _warning == null ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string? IStatusProvider.GetStatusMessage() => _warning;

    private const string NoSurfaceWarning = "Pick a surface or region.";
    private const string MissingSurfaceWarning = "The surface is not in the active output setup.";

    private const string OffStageWarning =
        "'{0}' has no stage position, so it is drawn where the surface the projector camera was solved from stands. "
        + "Set its Facing and Position in the output setup.";

    /** 0..1 over the rectangle, v up, in the corner order bottom-left, bottom-right, top-right, top-left. */
    private static readonly Vector2[] _cornerFractions = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];

    private readonly MeshGeometry _geometry = new()
                                                  {
                                                      Positions = new Vector3[4],
                                                      FaceCornerOffsets = [0, 4],
                                                      CornerPointIndices = new int[4],
                                                      Parts = new GeometryPart[1],
                                                  };

    private string? _warningFormat;
    private string? _warningSurfaceName;
    private string? _warning;

    [Input(Guid = "51069c93-bbfa-46c4-a98a-e4b2503060a2")]
    public readonly InputSlot<Guid> Surface = new();

    [Input(Guid = "bc23e0b9-060f-4ce2-89bc-70956f73575f")]
    public readonly InputSlot<float> Surround = new();
}
