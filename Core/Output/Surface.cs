#nullable enable
using System;
using System.Collections.Generic;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using T3.Serialization;

namespace T3.Core.Output;

/// <summary>
/// A projection target: physical size, rectified ContentCanvas, per-output mappings, and
/// optional reference-image / stage upgrades. The calibration ladder is monotone in data —
/// upgrading a surface adds fields, never rewrites existing ones.
/// </summary>
public sealed class Surface
{
    public static class SurfaceTypes
    {
        public const string Rect = "Rect";
    }

    public static class SurfaceKinds
    {
        /// <summary>A real plane placed in the stage — meters, own pose, calibratable.</summary>
        public const string Physical = "Physical";

        /// <summary>A coplanar child arranged in pixels; no independent pose, inherits its parent's plane.</summary>
        public const string Layout = "Layout";
    }

    /// <summary>ContentCanvas → OutputCanvas transfer for one output (L1/L2 corner pin).</summary>
    public sealed class OutputMapping
    {
        public static class Modes
        {
            public const string CornerPin = "CornerPin";
        }

        public Guid OutputId;
        public string Mode = Modes.CornerPin;

        /// <summary>Corners in OutputCanvas pixels: top-left, top-right, bottom-right, bottom-left of the content canvas.</summary>
        public Vector2[] Quad = new Vector2[4];

        public void WriteToJson(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteObject("OutputId", OutputId);
            writer.WriteString("Mode", Mode);
            writer.WriteQuad("Quad", Quad);
            writer.WriteEndObject();
        }

        public static OutputMapping ReadFromJson(JToken token)
        {
            return new OutputMapping
                       {
                           OutputId = OutputJson.ReadGuid(token["OutputId"]),
                           Mode = token.ReadValueSafe("Mode", Modes.CornerPin) ?? Modes.CornerPin,
                           Quad = OutputJson.ReadQuad(token["Quad"]),
                       };
        }
    }

    /// <summary>L2 upgrade: where this surface was traced on a reference image, plus its measurements.</summary>
    public sealed class ReferenceBinding
    {
        public Guid ImageId;

        /// <summary>Corners in reference-image pixels, same winding as <see cref="OutputMapping.Quad"/>.</summary>
        public Vector2[] Quad = new Vector2[4];

        public List<LineAnnotation> Annotations = [];

        public void WriteToJson(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteObject("ImageId", ImageId);
            writer.WriteQuad("Quad", Quad);
            writer.WritePropertyName("Annotations");
            writer.WriteStartArray();
            foreach (var annotation in Annotations)
                annotation.WriteToJson(writer);

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        public static ReferenceBinding ReadFromJson(JToken token)
        {
            return new ReferenceBinding
                       {
                           ImageId = OutputJson.ReadGuid(token["ImageId"]),
                           Quad = OutputJson.ReadQuad(token["Quad"]),
                           Annotations = token.ReadListSafe("Annotations", LineAnnotation.ReadFromJson),
                       };
        }
    }

    /// <summary>
    /// L3 upgrade: placement in the stage. Position is the world position of the <see cref="Anchor"/>;
    /// axis-aligned presets are editing rigs over the pose, not a storage format.
    /// </summary>
    public sealed class StagePlacement
    {
        public Pose Pose = Pose.Identity;

        public void WriteToJson(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteVector3("Position", Pose.Position);
            writer.WriteQuaternion("Orientation", Pose.Orientation);
            writer.WriteEndObject();
        }

        public static StagePlacement ReadFromJson(JToken token)
        {
            return new StagePlacement
                       {
                           Pose = new Pose(OutputJson.ReadVector3(token["Position"]),
                                           OutputJson.ReadQuaternion(token["Orientation"])),
                       };
        }
    }

    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public string Type = SurfaceTypes.Rect;

    /// <summary>Physical (own stage pose, meters) vs Layout (coplanar child, pixels) — see <see cref="SurfaceKinds"/>.</summary>
    public string Kind = SurfaceKinds.Physical;

    /// <summary>Parent surface for nesting; <see cref="Guid.Empty"/> for a root. A Layout child inherits its parent's plane.</summary>
    public Guid ParentId;

    /// <summary>
    /// For a Layout child: its bottom-left corner in meters from the *parent's anchor*, X right and Y up.
    /// Anchoring to the parent's grid origin — rather than normalizing to the parent's rect — is what keeps
    /// sub-regions welded to the meter raster when the parent is cropped or stretched.
    /// </summary>
    public Vector2 LocalPosition;

    /// <summary>When false the output manager skips this surface (kept in the setup, just not drawn).</summary>
    public bool Render = true;

    /// <summary>The <see cref="Slice"/> this surface shows; <see cref="Guid.Empty"/> for none. Several
    /// surfaces may name the same slice (the feed mirrored), and a surface shows at most one.</summary>
    public Guid SliceId;

    /// <summary>Physical size in meters. Defines the ContentCanvas aspect.</summary>
    public Vector2 SizeInMeters = new(1, 1);

    /// <summary>
    /// The surface's anchor, signed and centred: (0,0) is the centre, (0,−1) the bottom-centre, (±1,±1) the
    /// corners, Y up. It is the origin of the surface's own space — measuring lines, child regions and the
    /// metre raster are all measured from it — and the point a stage placement positions.
    /// </summary>
    public Vector2 Anchor = DefaultAnchor;

    /// <summary>Bottom-centre: a surface stands on the floor line by default.</summary>
    public static readonly Vector2 DefaultAnchor = new(0, -1);

    /// <summary>Where the anchor sits in metres from the surface's bottom-left corner.</summary>
    public Vector2 AnchorInMeters => (Anchor + Vector2.One) * 0.5f * SizeInMeters;

    /// <summary>When set, resizing keeps the current width/height ratio: editing one dimension solves the other.</summary>
    public bool LockAspect;

    /// <summary>Projects a real-world calibration raster over this surface (no content needed) so its
    /// corner-pin can be hand-aligned to physical wall features. Major lines are one meter apart and start at
    /// the <see cref="Anchor"/>, so the raster doubles as a ruler you can match to marks on the wall.</summary>
    public bool ShowGrid;

    /// <summary>Minor raster lines per meter; 1 draws meter lines only. They fade out once too dense to resolve.</summary>
    public int GridSubdivisions = 10;

    /// <summary>
    /// ContentCanvas resolution policy: pixel size is derived (px/m × physical size, clamped),
    /// never stored — surfaces get resized during calibration and content must survive it.
    /// </summary>
    public float PixelsPerMeter = 400;

    /// <summary>
    /// Measuring lines in <b>surface space</b> (meters, origin at the <see cref="Anchor"/>, Y up) — drawn across features of
    /// the projected raster that were measured for real. Distinct from <see cref="ReferenceBinding.Annotations"/>,
    /// which live in reference-photo pixels: these say how big this surface actually is, and "apply lengths"
    /// re-meters the surface from them without moving anything on the wall.
    /// </summary>
    public List<LineAnnotation> Annotations = [];

    public List<OutputMapping> OutputMappings = [];
    public ReferenceBinding? Reference;
    public StagePlacement? Placement;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Name", Name);
        writer.WriteString("Type", Type);
        writer.WriteString("Kind", Kind);
        writer.WriteObject("ParentId", ParentId);
        writer.WriteVector2("LocalPosition", LocalPosition);
        writer.WriteValue("Render", Render);
        writer.WriteObject("SliceId", SliceId);
        writer.WriteVector2("SizeInMeters", SizeInMeters);
        writer.WriteVector2("Anchor", Anchor);
        writer.WriteValue("LockAspect", LockAspect);
        writer.WriteValue("PixelsPerMeter", PixelsPerMeter);
        writer.WriteValue("ShowGrid", ShowGrid);
        writer.WriteValue("GridSubdivisions", GridSubdivisions);

        if (Annotations.Count > 0)
        {
            writer.WritePropertyName("Annotations");
            writer.WriteStartArray();
            foreach (var annotation in Annotations)
                annotation.WriteToJson(writer);

            writer.WriteEndArray();
        }

        writer.WritePropertyName("OutputMappings");
        writer.WriteStartArray();
        foreach (var mapping in OutputMappings)
            mapping.WriteToJson(writer);

        writer.WriteEndArray();

        if (Reference != null)
        {
            writer.WritePropertyName("Reference");
            Reference.WriteToJson(writer);
        }

        if (Placement != null)
        {
            writer.WritePropertyName("Placement");
            Placement.WriteToJson(writer);
        }

        writer.WriteEndObject();
    }

    public static Surface ReadFromJson(JToken token)
    {
        var surface = new Surface
                          {
                              Id = OutputJson.ReadGuid(token["Id"]),
                              Name = token.ReadValueSafe("Name", string.Empty) ?? string.Empty,
                              Type = token.ReadValueSafe("Type", SurfaceTypes.Rect) ?? SurfaceTypes.Rect,
                              Kind = token.ReadValueSafe("Kind", SurfaceKinds.Physical) ?? SurfaceKinds.Physical,
                              ParentId = OutputJson.ReadGuid(token["ParentId"]),
                              LocalPosition = OutputJson.ReadVector2(token["LocalPosition"], Vector2.Zero),
                              Render = token.ReadValueSafe("Render", true),
                              SliceId = OutputJson.ReadGuid(token["SliceId"]),
                              SizeInMeters = OutputJson.ReadVector2(token["SizeInMeters"], new Vector2(1, 1)),
                              Anchor = OutputJson.ReadVector2(token["Anchor"], DefaultAnchor),
                              LockAspect = token.ReadValueSafe("LockAspect", false),
                              PixelsPerMeter = token.ReadValueSafe("PixelsPerMeter", 400f),
                              ShowGrid = token.ReadValueSafe("ShowGrid", false),
                              GridSubdivisions = token.ReadValueSafe("GridSubdivisions", 10),
                              Annotations = token.ReadListSafe("Annotations", LineAnnotation.ReadFromJson),
                              OutputMappings = token.ReadListSafe("OutputMappings", OutputMapping.ReadFromJson),
                          };

        if (token["Reference"] is JObject referenceToken)
            surface.Reference = ReferenceBinding.ReadFromJson(referenceToken);

        if (token["Placement"] is JObject placementToken)
            surface.Placement = StagePlacement.ReadFromJson(placementToken);

        return surface;
    }
}
