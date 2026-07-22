#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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

    public Guid Id = Guid.NewGuid();
    public string Name = string.Empty;
    public string Kind = Kinds.Display;
    public Int2 CanvasResolution = new(1920, 1080);
    public ProjectorCamera? Camera;

    /// <summary>Pause presenting to this output without dropping its device binding (e.g. mute an NDI feed).</summary>
    public bool Send = true;

    public void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteObject("Id", Id);
        writer.WriteString("Name", Name);
        writer.WriteString("Kind", Kind);
        writer.WriteInt2("CanvasResolution", CanvasResolution);
        writer.WriteValue("Send", Send);
        if (Camera != null)
        {
            writer.WritePropertyName("Camera");
            Camera.WriteToJson(writer);
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
                         };

        if (token["Camera"] is JObject cameraToken)
            output.Camera = ProjectorCamera.ReadFromJson(cameraToken);

        return output;
    }
}
