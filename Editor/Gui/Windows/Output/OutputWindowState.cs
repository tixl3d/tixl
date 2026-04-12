#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using T3.Core.DataTypes;
using T3.Core.Operator;

namespace T3.Editor.Gui.Windows.Output;

/// <summary>
/// Persisted per-symbol state for an OutputWindow instance.
/// Stored as an array under "OutputWindows" in the .t3ui Settings block.
/// </summary>
internal sealed class OutputWindowState
{
    public static readonly OutputWindowState Defaults = new();

    // Gizmo state
    [JsonConverter(typeof(StringEnumConverter))]
    public GizmoVisibility ShowGizmos = GizmoVisibility.On;

    [JsonConverter(typeof(StringEnumConverter))]
    public TransformGizmoModes TransformGizmoMode = TransformGizmoModes.Move;

    // Background
    public float[] BackgroundColor = [0.1f, 0.1f, 0.1f, 1.0f];

    // Camera
    [JsonConverter(typeof(StringEnumConverter))]
    public CameraControlModes CameraControlMode = CameraControlModes.AutoUseFirstCam;

    public float[] CameraPosition = [0, 0, 2.4142134f]; // DefaultCameraDistance
    public float[] CameraTarget = [0, 0, 0];
    public float CameraRoll;
    public float CameraSpeed = 1;

    // Resolution
    public string? ResolutionTitle;
    public int ResolutionWidth;
    public int ResolutionHeight;
    public bool ResolutionUseAsAspectRatio;

    // Pinning
    public bool IsPinned;
    public Guid[] PinnedInstancePath = [];
    public Guid PinnedOutputId = Guid.Empty;

    /// <summary>
    /// Camera control modes — mirrors CameraSelectionHandling.ControlModes
    /// but as a public enum for serialization.
    /// </summary>
    public enum CameraControlModes
    {
        SceneViewerFollowing,
        UseViewer,
        AutoUseFirstCam,
        PickedACamera,
    }

    #region Serialization

    internal void WriteToJson(JsonTextWriter writer)
    {
        writer.WriteRawValue(JsonConvert.SerializeObject(this, Formatting.Indented));
    }

    internal static OutputWindowState? ReadFromJson(JToken token)
    {
        return token.ToObject<OutputWindowState>();
    }

    internal static void WriteAllToJson(JsonTextWriter writer, List<OutputWindowState>? states)
    {
        if (states == null || states.Count == 0)
            return;

        writer.WritePropertyName("OutputWindows");
        writer.WriteStartArray();
        foreach (var state in states)
        {
            state.WriteToJson(writer);
        }
        writer.WriteEndArray();
    }

    internal static List<OutputWindowState>? ReadAllFromJson(JToken? settingsToken)
    {
        if (settingsToken == null)
            return null;

        var arrayToken = settingsToken["OutputWindows"] as JArray;
        if (arrayToken == null)
            return null;

        var result = new List<OutputWindowState>();
        foreach (var item in arrayToken)
        {
            var state = ReadFromJson(item);
            if (state != null)
                result.Add(state);
        }

        return result.Count > 0 ? result : null;
    }

    #endregion
}
