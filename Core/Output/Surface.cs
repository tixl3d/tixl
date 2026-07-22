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
    /// L3 upgrade: placement in the stage. Position is the world position of the pivot;
    /// axis-aligned presets are editing rigs over the pose, not a storage format.
    /// </summary>
    public sealed class StagePlacement
    {
        public Pose Pose = Pose.Identity;

        /// <summary>Placement/scaling anchor, normalized [0..1] from the surface's bottom-left.</summary>
        public Vector2 Pivot;

        public void WriteToJson(JsonTextWriter writer)
        {
            writer.WriteStartObject();
            writer.WriteVector3("Position", Pose.Position);
            writer.WriteQuaternion("Orientation", Pose.Orientation);
            writer.WriteVector2("Pivot", Pivot);
            writer.WriteEndObject();
        }

        public static StagePlacement ReadFromJson(JToken token)
        {
            return new StagePlacement
                       {
                           Pose = new Pose(OutputJson.ReadVector3(token["Position"]),
                                           OutputJson.ReadQuaternion(token["Orientation"])),
                           Pivot = OutputJson.ReadVector2(token["Pivot"]),
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

    /// <summary>Optional compact label for gutters/badges; empty = auto-abbreviate from <see cref="Name"/>.</summary>
    public string ShortName = string.Empty;

    /// <summary>When false the output manager skips this surface (kept in the setup, just not drawn).</summary>
    public bool Render = true;

    /// <summary>Physical size in meters. Defines the ContentCanvas aspect.</summary>
    public Vector2 SizeInMeters = new(1, 1);

    /// <summary>Projects a real-world calibration raster over this surface (no content needed) so its
    /// corner-pin can be hand-aligned to physical wall features.</summary>
    public bool ShowGrid;

    /// <summary>Raster cell size in meters (default 25×25 cm). Cell counts derive from <see cref="SizeInMeters"/>.</summary>
    public Vector2 GridCellSize = new(0.25f, 0.25f);

    /// <summary>When set, editing one grid-cell dimension scales the other proportionally.</summary>
    public bool GridCellLinked = true;

    /// <summary>
    /// ContentCanvas resolution policy: pixel size is derived (px/m × physical size, clamped),
    /// never stored — surfaces get resized during calibration and content must survive it.
    /// </summary>
    public float PixelsPerMeter = 400;

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
        writer.WriteString("ShortName", ShortName);
        writer.WriteValue("Render", Render);
        writer.WriteVector2("SizeInMeters", SizeInMeters);
        writer.WriteValue("PixelsPerMeter", PixelsPerMeter);
        writer.WriteValue("ShowGrid", ShowGrid);
        writer.WriteVector2("GridCellSize", GridCellSize);
        writer.WriteValue("GridCellLinked", GridCellLinked);

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
                              ShortName = token.ReadValueSafe("ShortName", string.Empty) ?? string.Empty,
                              Render = token.ReadValueSafe("Render", true),
                              SizeInMeters = OutputJson.ReadVector2(token["SizeInMeters"], new Vector2(1, 1)),
                              PixelsPerMeter = token.ReadValueSafe("PixelsPerMeter", 400f),
                              ShowGrid = token.ReadValueSafe("ShowGrid", false),
                              GridCellSize = OutputJson.ReadVector2(token["GridCellSize"], new Vector2(0.25f, 0.25f)),
                              GridCellLinked = token.ReadValueSafe("GridCellLinked", true),
                              OutputMappings = token.ReadListSafe("OutputMappings", OutputMapping.ReadFromJson),
                          };

        if (token["Reference"] is JObject referenceToken)
            surface.Reference = ReferenceBinding.ReadFromJson(referenceToken);

        if (token["Placement"] is JObject placementToken)
            surface.Placement = StagePlacement.ReadFromJson(placementToken);

        return surface;
    }
}
