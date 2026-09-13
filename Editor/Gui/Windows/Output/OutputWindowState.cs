#nullable enable
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using T3.Editor.Gui.Windows.OutputSetup;
using T3.Core.DataTypes;
using T3.Core.Operator;
using T3.Editor.UiModel.Selection;

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

    /// <summary>
    /// Writes values into <paramref name="target"/>, reusing it when it already has the right length. The window
    /// syncs its state every frame, so replacing these arrays each time would allocate per frame per window.
    /// </summary>
    internal static void CopyInto(ref float[] target, float x, float y, float z)
    {
        if (target.Length != 3)
            target = new float[3];

        target[0] = x;
        target[1] = y;
        target[2] = z;
    }

    /// <inheritdoc cref="CopyInto(ref float[], float, float, float)"/>
    internal static void CopyInto(ref float[] target, System.Numerics.Vector4 value)
    {
        if (target.Length != 4)
            target = new float[4];

        target[0] = value.X;
        target[1] = value.Y;
        target[2] = value.Z;
        target[3] = value.W;
    }

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

    // Setup-entity pin (the setup-editing view's pin; orthogonal to the op-instance pin above)
    [JsonConverter(typeof(StringEnumConverter))]
    public SetupEntityKinds PinnedEntityKind = SetupEntityKinds.None;

    public Guid PinnedEntityId = Guid.Empty;

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
