#nullable enable
using System.Collections.Generic;
using System.Numerics;
using ImGuiNET;
using SilkWindows;
using T3.SystemUi;

namespace T3.Player;

/// <summary>
/// The modal window shown before the player creates its render window: display, resolution, window mode,
/// log visibility. <see cref="Result"/> is null when the user quit instead of starting.
/// </summary>
internal sealed class PlayerStartupDialog : IImguiDrawer<PlayerStartupOptions?>
{
    public PlayerStartupDialog(string applicationTitle, string author, IReadOnlyList<DisplayInfo> displays, PlayerStartupOptions initial)
    {
        _applicationTitle = applicationTitle;
        _author = author;
        _displays = displays;
        _options = initial.Clone();

        var display = _options.ResolveDisplay(displays);
        _selectedDisplayIndex = IndexOf(display);
        UpdateModesForSelectedDisplay();
    }

    public PlayerStartupOptions? Result => _closed ? _result : null;

    public void Init()
    {
    }

    public void OnRender(string windowName, double deltaSeconds, ImFonts fonts)
    {
        var padding = ImGui.GetStyle().WindowPadding.X * 2;
        ImGui.SetCursorPosX(padding);
        ImGui.BeginGroup();

        ImGui.PushFont(fonts.Large);
        ImGui.TextUnformatted(_applicationTitle);
        ImGui.PopFont();

        if (!string.IsNullOrEmpty(_author))
        {
            ImGui.PushFont(fonts.Small);
            ImGui.TextDisabled(_author);
            ImGui.PopFont();
        }

        ImGui.NewLine();
        ImGui.PushFont(fonts.Regular);

        var labelWidth = 110 * GetScale(fonts);
        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X - labelWidth - padding);

        // Display
        DrawLabel("Display", labelWidth);
        var selectedDisplay = _displays[_selectedDisplayIndex];
        if (ImGui.BeginCombo("##display", selectedDisplay.ToString()))
        {
            for (var index = 0; index < _displays.Count; index++)
            {
                var isSelected = index == _selectedDisplayIndex;
                if (ImGui.Selectable(_displays[index].ToString(), isSelected))
                {
                    _selectedDisplayIndex = index;
                    UpdateModesForSelectedDisplay();
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            ImGui.EndCombo();
        }

        // Resolution
        DrawLabel("Resolution", labelWidth);
        var currentLabel = _selectedModeIndex >= 0 ? _modeLabels[_selectedModeIndex] : CustomLabel;
        if (ImGui.BeginCombo("##resolution", currentLabel))
        {
            for (var index = 0; index < _modeLabels.Count; index++)
            {
                var isSelected = index == _selectedModeIndex;
                if (ImGui.Selectable(_modeLabels[index], isSelected))
                {
                    _selectedModeIndex = index;
                    var mode = _modes[index];
                    _options.Width = mode.Width;
                    _options.Height = mode.Height;
                }

                if (isSelected)
                    ImGui.SetItemDefaultFocus();
            }

            if (ImGui.Selectable(CustomLabel, _selectedModeIndex < 0))
            {
                _selectedModeIndex = -1;
            }

            ImGui.EndCombo();
        }

        if (_selectedModeIndex < 0)
        {
            DrawLabel(string.Empty, labelWidth);
            var itemWidth = ImGui.CalcItemWidth();
            ImGui.PushItemWidth(itemWidth * 0.5f - ImGui.GetStyle().ItemSpacing.X * 0.5f);
            if (ImGui.InputInt("##width", ref _options.Width, 0, 0))
                _options.Width = System.Math.Clamp(_options.Width, 16, 16384);

            ImGui.SameLine();
            if (ImGui.InputInt("##height", ref _options.Height, 0, 0))
                _options.Height = System.Math.Clamp(_options.Height, 16, 16384);

            ImGui.PopItemWidth();
        }

        ImGui.PopItemWidth();
        ImGui.NewLine();

        DrawLabel(string.Empty, labelWidth);
        ImGui.Checkbox("Fullscreen", ref _options.Fullscreen);
        DrawLabel(string.Empty, labelWidth);
        ImGui.Checkbox("Show log messages", ref _options.ShowLogs);

        ImGui.NewLine();
        ImGui.NewLine();

        // Buttons
        var buttonWidth = 120 * GetScale(fonts);
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - buttonWidth * 2 - spacing - padding);
        if (ImGui.Button("Quit", new Vector2(buttonWidth, 0)) || ImGui.IsKeyPressed(ImGuiKey.Escape))
        {
            Close(null);
        }

        ImGui.SameLine();
        if (ImGui.Button("Start", new Vector2(buttonWidth, 0)) || ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter))
        {
            Close(_options);
        }

        ImGui.PopFont();
        ImGui.EndGroup();
    }

    public void OnWindowUpdate(double deltaSeconds, out bool shouldClose)
    {
        shouldClose = _closed;
    }

    public void OnClose()
    {
        // Closing via the title bar counts as quitting
        _closed = true;
    }

    public void OnFileDrop(string[] filePaths)
    {
    }

    public void OnWindowFocusChanged(bool changedTo)
    {
    }

    private void Close(PlayerStartupOptions? result)
    {
        if (result != null)
        {
            var display = _displays[_selectedDisplayIndex];
            result.DisplayIndex = display.Index;
            result.DisplayName = display.Name;
        }

        _result = result;
        _closed = true;
    }

    private void UpdateModesForSelectedDisplay()
    {
        var display = _displays[_selectedDisplayIndex];
        _modes.Clear();
        _modeLabels.Clear();
        _selectedModeIndex = -1;
        for (var index = 0; index < display.Modes.Count; index++)
        {
            var mode = display.Modes[index];
            _modes.Add(mode);
            var label = mode.Width + " x " + mode.Height;
            if (mode.Width == display.CurrentMode.Width && mode.Height == display.CurrentMode.Height)
                label += " (native)";

            _modeLabels.Add(label);
            if (mode.Width == _options.Width && mode.Height == _options.Height)
                _selectedModeIndex = index;
        }
    }

    private int IndexOf(DisplayInfo display)
    {
        for (var index = 0; index < _displays.Count; index++)
        {
            if (ReferenceEquals(_displays[index], display))
                return index;
        }

        return 0;
    }

    private static void DrawLabel(string label, float width)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine(width);
    }

    private static float GetScale(ImFonts fonts) => fonts.HasFonts ? fonts.Regular.FontSize / 18f : 1f;

    private const string CustomLabel = "Custom...";

    private readonly string _applicationTitle;
    private readonly string _author;
    private readonly IReadOnlyList<DisplayInfo> _displays;
    private readonly PlayerStartupOptions _options;
    private readonly List<DisplayMode> _modes = [];
    private readonly List<string> _modeLabels = [];
    private int _selectedDisplayIndex;
    private int _selectedModeIndex = -1;
    private PlayerStartupOptions? _result;
    private bool _closed;
}
