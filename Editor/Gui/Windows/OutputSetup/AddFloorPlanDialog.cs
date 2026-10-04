#nullable enable
using ImGuiNET;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// Asks for the rectangle a floor plan starts from; walls are raised per edge on the plan's card afterwards.
/// Shared by the places a venue can be started: the strip header's "+" and the Board's own menu. A popup opens
/// in the window that draws it, so each host requests it and draws it — the numbers and the layout live here.
/// </summary>
internal static class AddFloorPlanDialog
{
    /// <summary>
    /// Opens the dialog on the next frame of the host that calls it — a popup can't open from inside a menu.
    /// </summary>
    /// <param name="boardPosition">Where the plan's card lands, in board metres; null leaves it to the Board.</param>
    public static void RequestOpen(Vector2? boardPosition = null)
    {
        _openRequested = true;
        _boardPosition = boardPosition;
    }

    /// <summary>Draws the dialog in the calling window, opening it first if it was requested there.</summary>
    public static void Draw(SetupEntitySelection selection)
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(DialogId);
        }

        ImGui.SetNextWindowSize(new Vector2(280 * T3Ui.UiScaleFactor, 0));
        if (!SetupPopup.Begin(DialogId))
            return;

        CustomComponents.StylizedText("Add Floor Plan", Fonts.FontBold, UiColors.Text);
        CustomComponents.StylizedText("A rectangle to start from; raise walls on its edges on its card.",
                                      Fonts.FontSmall, UiColors.TextMuted);

        FormInputs.AddFloat("Width (m)", ref _roomWidth, 0.1f, 1000, 0.05f, clampMin: true, clampMax: true, "Left to right.");
        FormInputs.AddFloat("Depth (m)", ref _roomDepth, 0.1f, 1000, 0.05f, clampMin: true, clampMax: true, "Near to far.");
        FormInputs.AddCheckBox("With floor surface", ref _roomWithFloor, "A surface lying on the footprint, for floor projection.");

        FormInputs.AddVerticalSpace(4);
        if (ImGui.Button("Create"))
        {
            SetupActions.AddFloorPlan(selection, new Vector2(_roomWidth, _roomDepth), _roomWithFloor, _boardPosition);
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
            ImGui.CloseCurrentPopup();

        SetupPopup.End();
    }

    private const string DialogId = "##addFloorPlanDialog";
    private static bool _openRequested;
    private static Vector2? _boardPosition;

    // Kept between openings so a venue's numbers can be tweaked and re-added.
    private static float _roomWidth = 10;
    private static float _roomDepth = 8;
    private static bool _roomWithFloor = true;
}
