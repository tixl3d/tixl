#nullable enable
using System;
using System.Collections.Generic;
using ImGuiNET;
using T3.Graphics.Compat;
using T3.Core.DataTypes;
using T3.Core.Logging;
using T3.Core.Operator;
using T3.Core.Output;
using T3.Core.Resource;
using T3.Core.Operator.Slots;
using T3.Editor.Gui.Dialog;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui;

/// <summary>
/// The master laser enable / emergency button in the app menu bar. Appears whenever a
/// laser operator is placed in any opened project. A plain click toggles the global
/// <see cref="LaserMasterEnable"/> state - all laser output operators blank their frames
/// while it is off.
/// </summary>
internal static class LaserMasterButton
{
    internal static void Draw()
    {
        CheckForLaserOperatorsThrottled();
        if (!_laserOpsDetected)
            return;

        ImGui.SameLine(0, 20);

        var enabled = LaserMasterEnable.Enabled;
        var frameHeight = ImGui.GetFrameHeight();
        var ctrlHeld = ImGui.GetIO().KeyCtrl;

        // Orange = master on while a laser output is still uncalibrated: the safety
        // beam (calibration rectangle) is what is being sent, not show content.
        // _safetyPending comes from the throttled check below - walking all opened
        // projects every frame was both wasteful and flooded the log.
        var safetyPending = enabled && _safetyPending;

        if (!_faceMode)
        {
            var color = !enabled ? DisabledColor
                      : safetyPending ? SafetySetupColor
                      : EnabledColor;
            ImGui.PushStyleColor(ImGuiCol.Button, color);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, color * 1.2f);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, color * 0.85f);
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0f, 0f, 0f, 1f));
            ImGui.Button("LASER", new Vector2(frameHeight * 2.2f, frameHeight));
            ImGui.PopStyleColor(4);
        }
        else
        {
            // Firing face when enabled; when disabled, the surprised open mouth while
            // hovered, otherwise the sad closed mouth. Uses the hover state captured
            // after the previous frame's dummy - a one-frame lag, imperceptible here.
            var srv = enabled ? SharedResources.LaserMasterFireImageSrv
                      : _hovered ? SharedResources.LaserMasterHoverImageSrv
                      : SharedResources.LaserMasterIdleImageSrv;
            if (srv == null)
            {
                ImGui.TextUnformatted("LASER ?");
            }
            else
            {
                var aspect = GetAspect(srv);
                var size = new Vector2(frameHeight * aspect, frameHeight);
                var pos = ImGui.GetCursorScreenPos();
                ImGui.GetWindowDrawList().AddImage((IntPtr)srv.ImGuiTextureId, pos, pos + size);
                ImGui.Dummy(size);
            }
        }

        _hovered = ImGui.IsItemHovered();

        if (!ImGui.IsItemClicked(ImGuiMouseButton.Left))
        {
            DrawTooltip(enabled);
            return;
        }

        if (ctrlHeld)
        {
            _faceMode = !_faceMode;
        }
        else if (enabled)
        {
            LaserMasterEnable.Enabled = false;
            Log.Debug("Laser master enable -> DISABLED (red) - laser outputs blanked");
        }
        else if (UserSettings.Config.LaserSafetyWarningAcknowledged)
        {
            Arm();
        }
        else
        {
            // First arm (or the acknowledgement was declined): the safety warning gates the
            // master. The dialog arms the master itself when confirmed with OK.
            T3Ui.LaserArmWarningDialog.ShowNextFrame();
        }
    }

    private static void Arm()
    {
        LaserMasterEnable.Enabled = true;
        Log.Debug("Laser master enable -> ENABLED (green) - master armed, laser outputs stream");
    }

    private static void DrawTooltip(bool enabled)
    {
        if (!ImGui.IsItemHovered())
            return;

        var safetyPending = enabled && _safetyPending;
        ImGui.BeginTooltip();
        if (!enabled)
        {
            ImGui.TextUnformatted("Master laser enable: OFF - all laser output blanked");
        }
        else if (safetyPending)
        {
            ImGui.TextColored(SafetySetupColor, "SAFETY BEAM ACTIVE");
            ImGui.TextUnformatted("A laser output is not calibrated yet - only the");
            ImGui.TextUnformatted("alignment rectangle is being sent to the laser.");
            ImGui.TextUnformatted("Set Calibrated on the laser output for content.");
        }
        else
        {
            ImGui.TextUnformatted("Master laser enable: ON - content streaming");
        }
        ImGui.Separator();
        ImGui.TextUnformatted("Click: enable / disable all laser output");
        ImGui.EndTooltip();
    }

    /// <summary>
    /// True while a laser output of the active setup has uncalibrated processing settings — those
    /// stream only the alignment rectangle, not show content. (Safety is setup-managed; there is
    /// no graph-side LaserSafety op anymore.)
    /// </summary>
    private static bool AnyLaserSafetyUncalibrated()
    {
        var setup = T3.Core.Output.ActiveSetup.Current;
        if (setup == null)
            return false;

        foreach (var output in setup.Outputs)
        {
            if (output.Kind == OutputDefinition.Kinds.Laser
                && (output.LaserProcessing ?? LaserProcessingSettings.Default).Safety is { Calibrated: false })
                return true;
        }

        return false;
    }

    private static float GetAspect(ShaderResourceView srv)
    {
        if (srv.Resource is T3.Graphics.Compat.Texture2D texture)
            return (float)texture.Description.Width / texture.Description.Height;

        return 1f;
    }

    /// <summary>
    /// Walks the symbol trees of all opened projects (throttled - this is a UI-panel
    /// convenience, not something that needs to be instantaneous) and reports whether
    /// any operator from the laser namespace is placed anywhere in them.
    /// </summary>
    private static void CheckForLaserOperatorsThrottled()
    {
        var now = ImGui.GetTime();
        if (now - _lastCheckTime < CheckInterval)
            return;

        _lastCheckTime = now;
        var detected = DetectLaserOperators();
        if (detected != _laserOpsDetected)
        {
            _laserOpsDetected = detected;
            Log.Debug(detected
                          ? "Laser master button: laser operator found in an opened project - button visible"
                          : "Laser master button: no laser operators left - button hidden");
        }

        if (!detected)
        {
            _safetyPending = false;
            return;
        }

        var pending = AnyLaserSafetyUncalibrated();
        if (pending == _safetyPending)
            return;

        _safetyPending = pending;
        Log.Debug(pending
                      ? "Laser master button: safety setup pending - uncalibrated laser output found (button orange)"
                      : "Laser master button: all LaserSafety ops calibrated (button green when enabled)");
    }

    private static bool DetectLaserOperators()
    {
        foreach (var opened in OpenedProject.OpenedProjects.Values)
        {
            var package = opened.Package;
            if (!package.HasHomeSymbol(out _))
                continue;

            if (!package.Symbols.TryGetValue(package.HomeSymbolId, out var home) || home == null)
                continue;

            _visited.Clear();
            if (WalkSymbols(home))
                return true;
        }

        return false;
    }

    private static bool WalkSymbols(Symbol symbol)
    {
        if (!_visited.Add(symbol.Id))
            return false;

        foreach (var child in symbol.Children.Values)
        {
            var childSymbol = child.Symbol;
            if (childSymbol == null)
                continue;

            var ns = childSymbol.InstanceType?.Namespace;
            if (ns != null && ns.StartsWith("Lib.laser", StringComparison.Ordinal))
                return true;

            if (WalkSymbols(childSymbol))
                return true;
        }

        return false;
    }

    private static readonly HashSet<Guid> _visited = new();
    private static bool _laserOpsDetected;
    private static bool _safetyPending;
    private static bool _faceMode;
    private static bool _hovered;
    private static double _lastCheckTime = -CheckInterval;

    // The check walks every opened project's full symbol tree on the UI thread — cheap enough at
    // this cadence, a visible stutter at faster ones.
    private const double CheckInterval = 5.0;

    private static readonly Vector4 EnabledColor = new(0.25f, 0.80f, 0.35f, 1f);
    private static readonly Vector4 DisabledColor = new(0.90f, 0.16f, 0.16f, 1f);
    private static readonly Vector4 SafetySetupColor = new(0.95f, 0.55f, 0.10f, 1f);
}
