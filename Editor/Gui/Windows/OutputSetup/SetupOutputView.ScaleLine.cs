#nullable enable
using ImGuiNET;
using T3.Core.Output;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.CanvasEditing;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel.Selection;
using Vector2 = System.Numerics.Vector2;

namespace T3.Editor.Gui.Windows.OutputSetup;

/// <summary>
/// A plan image's scale line on its Board card: two points in image pixels and the real length between them,
/// from which the image's metres-per-pixel follows — so the card stands at true size next to the floor plan
/// it is traced into. "Set Scale" is a two-click tool on the card that ends in the length prompt; afterwards
/// the endpoints stay draggable and the length stays editable on the card.
/// </summary>
internal sealed partial class SetupOutputView
{
    /// <summary>Asks the next Board frame to start the scale tool on this image — set by the image's menu.</summary>
    public static Guid PendingScaleToolImageId;

    /// <summary>Whether the scale tool owns the Board's mouse right now.</summary>
    private bool IsSettingScale => _scaleToolImageId != Guid.Empty;

    /// <summary>The scale line on an image card, its handles, and the tool that draws a new one.</summary>
    private void DrawBoardScaleLine(Setup setup, ImDrawListPtr dl, ReferenceImage image, Vector2 cardMin, Vector2 cardMax)
    {
        if (PendingScaleToolImageId == image.Id)
        {
            _scaleToolImageId = image.Id;
            _scaleToolHasStart = false;
            PendingScaleToolImageId = Guid.Empty;
        }

        var scale = T3Ui.UiScaleFactor;
        var fade = _boardLayerFade;
        var interactive = fade >= 0.999f;
        var pixelsPerMeter = PixelsPerMeterOf(image.BoardPlacement, image);
        var hue = UiColors.StatusControlled;

        if (IsSettingScale && _scaleToolImageId == image.Id)
        {
            DrawScaleTool(setup, dl, image, cardMin, cardMax, pixelsPerMeter);
            return;
        }

        if (!image.HasScaleLine)
            return;

        var start = ImageToBoard(image.ScaleLineStart, cardMin, cardMax, pixelsPerMeter);
        var end = ImageToBoard(image.ScaleLineEnd, cardMin, cardMax, pixelsPerMeter);
        var a = _boardProjection.CanvasToScreen(start);
        var b = _boardProjection.CanvasToScreen(end);
        dl.AddLine(a, b, hue.Fade(0.9f * fade), 2 * scale);
        var along = b - a;
        if (along.LengthSquared() > 1f)
        {
            along /= along.Length();
            CanvasDraw.TextAlong(dl, Fonts.FontSmall, Fonts.FontSmall.FontSize, (a + b) * 0.5f + new Vector2(along.Y, -along.X) * 10 * scale, along,
                                 UiColors.Text.Fade(fade), $"{image.ScaleLineMeters:0.##} m");
        }

        // The endpoints re-place the line on the image; the scale follows on release, so the card doesn't
        // resize under the cursor mid-drag.
        if (!interactive || image.IsLocked)
            return;

        var style = CanvasPointHandle.Style.Default(hue, CanvasPointHandle.Shapes.Circle);
        ImGui.PushID(image.Id.GetHashCode());
        for (var endpoint = 0; endpoint < 2; endpoint++)
        {
            ImGui.PushID(endpoint);
            var pos = endpoint == 0 ? start : end;
            var phase = CanvasPointHandle.Draw(ref pos, _boardProjection, style);
            ImGui.PopID();
            switch (phase)
            {
                case CanvasPointHandle.DragPhases.Started:
                    BeginGesture(setup, GestureKinds.ScaleLine, "Move scale line", image.Id);
                    break;

                case CanvasPointHandle.DragPhases.Dragging when _gesture.Is(GestureKinds.ScaleLine, image.Id):
                    var px = BoardToImage(pos, cardMin, cardMax, pixelsPerMeter);
                    if (endpoint == 0)
                        image.ScaleLineStart = px;
                    else
                        image.ScaleLineEnd = px;

                    break;

                case CanvasPointHandle.DragPhases.Completed:
                    ApplyScaleLine(image);
                    EndGesture(setup);
                    break;
            }
        }

        ImGui.PopID();
    }

    /// <summary>First click starts the line, the second ends it and asks for the length; Escape or a right-click abandons.</summary>
    private void DrawScaleTool(Setup setup, ImDrawListPtr dl, ReferenceImage image, Vector2 cardMin, Vector2 cardMax, float pixelsPerMeter)
    {
        if (_scaleToolPopupOpen)
            return;

        var scale = T3Ui.UiScaleFactor;
        var hue = UiColors.StatusControlled;
        var focused = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && !ImGui.GetIO().WantTextInput;
        if (focused && (ImGui.IsKeyPressed(ImGuiKey.Escape, false) || ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
        {
            _scaleToolImageId = Guid.Empty;
            return;
        }

        var cursorOnBoard = _boardProjection.ScreenToCanvas(ImGui.GetMousePos());
        var cursor = ImGui.GetMousePos();
        if (_scaleToolHasStart)
        {
            var a = _boardProjection.CanvasToScreen(ImageToBoard(_scaleToolStart, cardMin, cardMax, pixelsPerMeter));
            dl.AddLine(a, cursor, hue.Fade(0.9f), 2 * scale);
            dl.AddCircleFilled(a, 4 * scale, hue);
        }

        dl.AddCircle(cursor, 5 * scale, hue, 0, 1.5f * scale);
        var hint = _scaleToolHasStart ? "Click the line's other end" : "Click where a known length starts";
        dl.AddText(Fonts.FontSmall, Fonts.FontSmall.FontSize, cursor + new Vector2(12 * scale, -6 * scale), UiColors.Text, hint);

        if (!ImGui.IsWindowHovered() || ImGui.IsAnyItemHovered() || !ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            return;

        var px = BoardToImage(cursorOnBoard, cardMin, cardMax, pixelsPerMeter);
        if (!_scaleToolHasStart)
        {
            _scaleToolStart = px;
            _scaleToolHasStart = true;
            return;
        }

        if ((px - _scaleToolStart).Length() < 1f)
            return;

        _scaleToolEnd = px;
        _scaleToolLength = image.HasScaleLine && image.ScaleLineMeters > 0 ? image.ScaleLineMeters : 1f;
        _scaleToolPopupOpen = true;
        ImGui.OpenPopup(ScaleLengthPopupId);
    }

    /// <summary>The length prompt at the end of the tool; drawn by the Board every frame so the popup can open.</summary>
    private void DrawScaleLengthPopup(Setup setup)
    {
        ImGui.SetNextWindowSize(new Vector2(260 * T3Ui.UiScaleFactor, 0));
        if (!ImGui.BeginPopup(ScaleLengthPopupId))
        {
            // Dismissed by a click elsewhere: the tool ends with its popup.
            if (_scaleToolPopupOpen)
            {
                _scaleToolPopupOpen = false;
                _scaleToolImageId = Guid.Empty;
            }

            return;
        }
        CustomComponents.StylizedText("Real length of the line", Fonts.FontBold, UiColors.Text);
        FormInputs.AddFloat("Length (m)", ref _scaleToolLength, 0.001f, 10000, 0.01f, clampMin: true, clampMax: true);

        FormInputs.AddVerticalSpace(4);
        var image = setup.FindReferenceImage(_scaleToolImageId);
        if (ImGui.Button("Apply") && image != null)
        {
            var start = _scaleToolStart;
            var end = _scaleToolEnd;
            var meters = _scaleToolLength;
            SetupUndo.RunUndoable("Set image scale", setup, () =>
                                                             {
                                                                 image.ScaleLineStart = start;
                                                                 image.ScaleLineEnd = end;
                                                                 image.ScaleLineMeters = meters;
                                                                 ApplyScaleLine(image);
                                                             });
            _scaleToolImageId = Guid.Empty;
            _scaleToolPopupOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.SameLine();
        if (ImGui.Button("Cancel"))
        {
            _scaleToolImageId = Guid.Empty;
            _scaleToolPopupOpen = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// Derives the image's metres-per-pixel from its scale line and lets go of any presentation scale on its
    /// card, so the card is drawn at true size.
    /// </summary>
    public static void ApplyScaleLine(ReferenceImage image)
    {
        var pixels = (image.ScaleLineEnd - image.ScaleLineStart).Length();
        if (pixels < 0.5f || image.ScaleLineMeters <= 0)
            return;

        image.MetersPerPixel = image.ScaleLineMeters / pixels;
        if (image.BoardPlacement != null)
            image.BoardPlacement.CardScale = 0;
    }

    /** Image pixels (top-left, Y down) to Board metres on this card. */
    private static Vector2 ImageToBoard(Vector2 px, Vector2 cardMin, Vector2 cardMax, float pixelsPerMeter)
    {
        return new Vector2(cardMin.X + px.X / pixelsPerMeter, cardMax.Y - px.Y / pixelsPerMeter);
    }

    private static Vector2 BoardToImage(Vector2 board, Vector2 cardMin, Vector2 cardMax, float pixelsPerMeter)
    {
        return new Vector2((board.X - cardMin.X) * pixelsPerMeter, (cardMax.Y - board.Y) * pixelsPerMeter);
    }

    private Guid _scaleToolImageId;
    private bool _scaleToolHasStart;
    private bool _scaleToolPopupOpen;
    private Vector2 _scaleToolStart;
    private Vector2 _scaleToolEnd;
    private float _scaleToolLength = 1f;
    private const string ScaleLengthPopupId = "##scaleLineLength";
}
