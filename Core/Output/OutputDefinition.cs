#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Numerics;
using T3.Core.DataTypes.Vector;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// "What am I rendering for?" — a named canvas with optional device binding (per machine,
/// stored separately) and optional calibrated camera. Covers the always-present Default,
/// format targets (no device), and physical projector/display outputs.
/// </summary>
public sealed class OutputDefinition
{
    public static class Kinds
    {
        public const string Default = "Default";
        public const string Format = "Format";
        public const string Projector = "Projector";
        public const string Display = "Display";
    }

    /// <summary>
    /// L3: a first-class camera authored by calibration — referencable by graph ops for any
    /// render, not just the output's own canvas. Pose/lens stay unset until solved.
    /// </summary>
    public sealed class ProjectorCamera
    {
        public Pose? Pose;
        public Projection? Lens;
        public List<CalibrationPoint> CalibrationPoints = [];

        /// <summary>Mean reprojection error in pixels of the last solve; 0 = never solved.</summary>
        public float ResidualPx;

        /// <summary>Manual camera used until a calibration solve fills <see cref="Pose"/>/<see cref="Lens"/>:
        /// a look-at (position → target) with a vertical field of view, so the render path works before calibration.</summary>
        public System.Numerics.Vector3 ManualPosition = new(0, 1, 3);
        public System.Numerics.Vector3 ManualTarget = System.Numerics.Vector3.Zero;
        public float ManualFovYDegrees = 45;

        public void WriteToJson(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            if (Pose is { } pose)
            {
                writer.WriteVector3("Position", pose.Position);
                writer.WriteQuaternion("Orientation", pose.Orientation);
            }

            if (Lens is { } lens)
            {
                writer.WriteValue("FieldOfViewY", lens.FieldOfViewY);
                writer.WriteVector2("LensShift", lens.LensShift);
            }

            writer.WriteVector3("ManualPosition", ManualPosition);
            writer.WriteVector3("ManualTarget", ManualTarget);
            writer.WriteValue("ManualFovY", ManualFovYDegrees);

            writer.WritePropertyName("CalibrationPoints");
            writer.WriteStartArray();
            foreach (var point in CalibrationPoints)
                point.WriteToJson(writer);

            writer.WriteEndArray();

            if (ResidualPx > 0)
                writer.WriteValue("ResidualPx", ResidualPx);

            writer.WriteEndObject();
        }

        public static ProjectorCamera ReadFromJson(JToken token)
        {
            var camera = new ProjectorCamera
                             {
                                 CalibrationPoints = token.ReadListSafe("CalibrationPoints", CalibrationPoint.ReadFromJson),
                                 ResidualPx = token.ReadValueSafe("ResidualPx", 0f),
                                 ManualPosition = OutputJson.ReadVector3(token["ManualPosition"], new System.Numerics.Vector3(0, 1, 3)),
                                 ManualTarget = OutputJson.ReadVector3(token["ManualTarget"]),
                                 ManualFovYDegrees = token.ReadValueSafe("ManualFovY", 45f),
                             };

            if (token["Position"] != null && token["Orientation"] != null)
            {
                camera.Pose = new Pose(OutputJson.ReadVector3(token["Position"]),
                                       OutputJson.ReadQuaternion(token["Orientation"]));
            }

            if (token["FieldOfViewY"] != null)
            {
                camera.Lens = Projection.CreatePerspective(token.ReadValueSafe("FieldOfViewY", 0f),
                                                           OutputJson.ReadVector2(token["LensShift"]));
            }

            return camera;
        }
    }

    /// <summary>
    /// A region of the output canvas fed by one <see cref="Slice"/>, bypassing surfaces: the direct pipe.
    /// Patches model the canvas in pixels — a full-frame feed, a split matrix, a picture-in-picture, or a
    /// surface-less keystone (a warped quad) — where surfaces model the room in metres. A route's quad lives
    /// on a patch <em>or</em> on a surface's mapping, never both.
    /// </summary>
    public sealed class Patch
    {
        public Guid Id = Guid.NewGuid();

        /// <summary>Empty = labelled by position ("Patch N"), so the default reads right after reordering.</summary>
        public string Name = string.Empty;

        /// <summary>The slice shown here; <see cref="Guid.Empty"/> while nothing is routed yet.</summary>
        public Guid SliceId;

        /// <summary>Where the slice lands on the canvas, in its 0..1 space (Y down): TL, TR, BR, BL.
        /// Axis-aligned for tiles; a warped quad is a keystone without a surface. Normalized for the same
        /// reason a mapping's quad is — see <see cref="Surface.OutputMapping.Quad"/>.</summary>
        public Vector2[] Quad = [];

        /// <summary>
        /// Quarter turns of the picture inside the quad, clockwise, 0..3 — for a display or LED panel mounted on
        /// its side. The quad itself does not turn: its corners stay TL, TR, BR, BL on the canvas, so every
        /// edit, snap and size field keeps working unchanged, and only which corner receives the source's
        /// top-left shifts when the patch is composited.
        /// </summary>
        public int QuarterTurns;

        /// <summary>Folds any integer into 0..3, so a rotate that keeps adding stays a valid turn count.</summary>
        public static int NormalizeTurns(int turns) => ((turns % 4) + 4) % 4;

        /// <summary>
        /// The quad's corners in the order the picture's corners land on them: the source's top-left goes to
        /// the first. Shifting by the turns is the whole rotation — the same four points, starting one corner
        /// further round per quarter turn.
        /// </summary>
        public void CopyTurnedCorners(Span<Vector2> destination)
        {
            var turns = NormalizeTurns(QuarterTurns);
            for (var c = 0; c < 4; c++)
                destination[c] = Quad[(c + turns) % 4];
        }

        public void WriteToJson(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteObject("Id", Id);
            if (!string.IsNullOrEmpty(Name))
                writer.WriteString("Name", Name);

            writer.WriteObject("SliceId", SliceId);
            writer.WriteQuad("Quad", Quad);
            if (QuarterTurns != 0)
                writer.WriteValue("QuarterTurns", QuarterTurns);

            writer.WriteEndObject();
        }

        public static Patch ReadFromJson(JToken token)
        {
            return new Patch
                       {
                           Id = OutputJson.ReadGuid(token["Id"]),
                           Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                           SliceId = OutputJson.ReadGuid(token["SliceId"]),
                           Quad = OutputJson.ReadQuad(token["Quad"]),
                           QuarterTurns = NormalizeTurns(token.ReadValueSafe("QuarterTurns", 0)),
                       };
        }
    }

    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public string Kind = Kinds.Display;
    /// <summary>
    /// The canvas' pixel size, or 0×0 to follow whatever plug presents this output — the display's mode, so the
    /// same setup renders at 1080p or 1440p depending on what is plugged in. Everything mapped onto the canvas
    /// is stored as a fraction of it, so this is only a render size and changing it moves nothing.
    /// </summary>
    public Int2 CanvasResolution = new(1920, 1080);

    /// <summary>Whether this canvas takes its size from the plug it is bound to.</summary>
    public bool FollowsPlug => CanvasResolution.Width <= 0 || CanvasResolution.Height <= 0;

    /// <summary>
    /// What the canvas actually renders at: <see cref="CanvasResolution"/> when it is set, else the bound
    /// plug's. Runtime only, never serialized — the binding is machine state the setup deliberately doesn't
    /// know, so the host resolves this once per frame (see OutputSetupHandling.UpdateFrame).
    /// </summary>
    public Int2 ResolvedResolution = new(1920, 1080);
    public ProjectorCamera? Camera;

    /// <summary>Pause presenting to this output without dropping its device binding (e.g. mute an NDI feed).</summary>
    public bool Send = true;

    /// <summary>Canvas regions on the direct pipe, composited in list order underneath the surfaces mapped here.</summary>
    public List<Patch> Patches = [];

    /// <summary>Its card's place on the Board; null until the Board seeded one.</summary>
    public CanvasPlacement? BoardPlacement;

    /// <summary>The whole canvas as a TL, TR, BR, BL quad in its 0..1 space — the rung-0 patch, and the reset shape.</summary>
    public static Vector2[] FullCanvasQuad()
    {
        return [Vector2.Zero, new Vector2(1, 0), Vector2.One, new Vector2(0, 1)];
    }

    /// <summary>The canvas in pixels, floored at 1 — the factor between stored 0..1 quads and what editors drag in.</summary>
    public Vector2 CanvasSize => new(Math.Max(1, ResolvedResolution.Width), Math.Max(1, ResolvedResolution.Height));

    /// <summary>A stored 0..1 point as canvas pixels.</summary>
    public Vector2 ToPixels(Vector2 normalized) => normalized * CanvasSize;

    /// <summary>Canvas pixels back into the stored 0..1 space.</summary>
    public Vector2 ToNormalized(Vector2 pixels) => pixels / CanvasSize;

    /// <summary>Reads a stored quad into <paramref name="into"/> as canvas pixels.</summary>
    public void ReadQuadInPixels(ReadOnlySpan<Vector2> quad, Span<Vector2> into)
    {
        var size = CanvasSize;
        for (var i = 0; i < 4 && i < quad.Length && i < into.Length; i++)
            into[i] = quad[i] * size;
    }

    /// <summary>Writes canvas pixels back into a stored 0..1 quad.</summary>
    public void WriteQuadFromPixels(ReadOnlySpan<Vector2> pixels, Span<Vector2> quad)
    {
        var size = CanvasSize;
        for (var i = 0; i < 4 && i < pixels.Length && i < quad.Length; i++)
            quad[i] = pixels[i] / size;
    }

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Name", Name);
        writer.WriteString("Kind", Kind);
        writer.WriteInt2("CanvasResolution", CanvasResolution);
        writer.WriteValue("Send", Send);
        if (Patches.Count > 0)
        {
            writer.WritePropertyName("Patches");
            writer.WriteStartArray();
            foreach (var patch in Patches)
                patch.WriteToJson(writer);

            writer.WriteEndArray();
        }

        if (Camera != null)
        {
            writer.WritePropertyName("Camera");
            Camera.WriteToJson(writer);
        }

        if (BoardPlacement != null)
        {
            writer.WritePropertyName("BoardPlacement");
            BoardPlacement.WriteToJson(writer);
        }

        writer.WriteEndObject();
    }

    public static OutputDefinition ReadFromJson(JToken token)
    {
        var output = new OutputDefinition
                         {
                             Id = OutputJson.ReadGuid(token["Id"]),
                             Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                             Kind = token.ReadValueSafe("Kind", Kinds.Display) ?? Kinds.Display,
                             CanvasResolution = OutputJson.ReadInt2(token["CanvasResolution"], new Int2(1920, 1080)),
                             Send = token.ReadValueSafe("Send", true),
                             Patches = token.ReadListSafe("Patches", Patch.ReadFromJson),
                         };

        if (token["Camera"] is JObject cameraToken)
            output.Camera = ProjectorCamera.ReadFromJson(cameraToken);

        if (token["BoardPlacement"] is JObject placement)
            output.BoardPlacement = CanvasPlacement.ReadFromJson(placement);

        return output;
    }
}
