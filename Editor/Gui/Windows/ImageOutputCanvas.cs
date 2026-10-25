#nullable enable
using System.Diagnostics;
using ImGuiNET;
using T3.Graphics;
using T3.Graphics.Compat;
using T3.Core.DataTypes;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiContentDrawing;
using T3.SystemUi;

namespace T3.Editor.Gui.Windows;

internal sealed class ImageOutputCanvas : ScalableCanvas
{
    public void Update(T3Ui.EditingFlags editingFlags = T3Ui.EditingFlags.None)
    {
        if (_isInteractionBlocked)
            editingFlags |= T3Ui.EditingFlags.PreventMouseInteractions;

        _isInteractionBlocked = false;
        UpdateCanvas(out var interactionState, editingFlags);
        UpdateViewMode(interactionState);
    }

    /// <summary>
    /// Lets an output UI that handles the mouse itself, like the orbiting cube map preview, keep the canvas from
    /// panning and zooming. Has to be called again every frame while drawing.
    /// </summary>
    internal void BlockInteractionThisFrame()
    {
        _isInteractionBlocked = true;
    }

    /// <summary>
    /// Keeps <paramref name="areaOnCanvas"/> fitted to the window while the view is in fitted mode, as
    /// <see cref="DrawTexture"/> does for a texture.
    /// </summary>
    internal void FitAreaIfFitted(ImRect areaOnCanvas)
    {
        if (_viewMode != Modes.Fitted)
            return;

        FitAreaOnCanvas(areaOnCanvas);
        if (DisableDamping)
        {
            Scale = ScaleTarget;
        }
    }

    public void SetAsCurrent()
    {
        Current = this;
    }

    public static void Deactivate()
    {
        Current = null;
    }

    /// <summary>
    /// The image canvas that is currently being drawn from the UI. 
    /// Note that <see cref="ImageOutputCanvas"/> is NOT a singleton so you can't rely on this to be valid outside of the Draw()ing context.
    /// It is used by <see cref="Texture2dOutputUi"/> to draw its content.
    /// </summary>
    public static ImageOutputCanvas? Current;

    protected override ScalableCanvas? Parent => null;

    public T3.Core.DataTypes.Texture2D? LastTexture;

    /// <summary>Set by the window when the shown op was already evaluated this frame but had to render again for
    /// this window's resolution preset; the caption then carries a warning.</summary>
    public bool IsRenderedTwice;
        
    public void DrawTexture(T3.Core.DataTypes.Texture2D? texture)
    {
        CustomComponents.FillWithStripes(ImGui.GetWindowDrawList(), DrawUtils.GetContentRegionArea(), Scale.X);
        LastTexture = texture;
            
        Debug.Assert(Current != null);
        
        if (texture == null || texture.IsDisposed)
            return;

        var size = new Vector2(texture.Description.Width, texture.Description.Height);
        Current.FitAreaIfFitted(new ImRect(0, 0, size.X, size.Y));

        var topLeft = Vector2.Zero;
        var topLeftOnScreen = Current.TransformPosition(topLeft);
        ImGui.SetCursorScreenPos(topLeftOnScreen);

        var sizeOnScreen = Current.TransformDirection(size);

        var srv = SrvManager.GetSrvForTexture(texture);
        var drawList = ImGui.GetWindowDrawList();
        WindowsUiContentDrawer.BeginPixelSampling(drawList);
        ImGui.Image((IntPtr)srv.ImGuiTextureId, sizeOnScreen);
        WindowsUiContentDrawer.EndPixelSampling(drawList);

        if (ImGui.IsMouseHoveringRect(topLeftOnScreen, topLeftOnScreen + sizeOnScreen))
        {
            var relativePosition = (ImGui.GetMousePos() - topLeftOnScreen) / sizeOnScreen;
            MouseInput.Set(relativePosition, ImGui.IsMouseDown(ImGuiMouseButton.Left));
        }
            
        if (UserSettings.Config.ShowToolbar)
        {
            var format = srv == null || srv.IsDisposed ? "null?" : GetFormatLabel(srv.Description.Format);
            var zoom = Math.Abs(Scale.X) < 0.001f ? "" : $" ×{Scale.X:G2}";
            DrawCaption($"{size.X}x{size.Y}  {format} {zoom}");
        }
    }

    internal static string GetFormatLabel(Format format)
    {
        if (UserSettings.Config.ShowExplicitTextureFormatInOutputWindow)
            return format.ToString();

        return format switch
                   {
                       Format.R16G16B16A16_Float => "RGBA:16",
                       Format.R8G8B8A8_SNorm     => "RGBA:8",
                       _                         => format.ToString(),
                   };
    }

    /// <summary>The line at the bottom of the window that describes what is shown.</summary>
    internal void DrawCaption(string description)
    {
        ImGui.PushFont(Fonts.FontSmall);
        var descriptionWidth = ImGui.CalcTextSize(description).X;

        var textPos = new Vector2(WindowPos.X + (WindowSize.X - descriptionWidth) / 2,
                                  WindowPos.Y + WindowSize.Y - 14 * T3Ui.UiScaleFactor);

        var drawList = ImGui.GetWindowDrawList();
        var shadowColor = UiColors.BackgroundFull.Fade(0.5f);
        drawList.AddText(textPos + new Vector2(1,0), shadowColor, description );
        drawList.AddText(textPos + new Vector2(-1,0), shadowColor, description );
        drawList.AddText(textPos + new Vector2(0,1), shadowColor, description );
        drawList.AddText(textPos + new Vector2(0,-1), shadowColor, description );
        drawList.AddText(textPos, UiColors.ForegroundFull, description );
        ImGui.PopFont();

        if (IsRenderedTwice)
            DrawRenderedTwiceWarning(drawList, textPos);
    }
        
    //private static readonly Color ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.6f);


    /// <summary>A warning glyph left of the caption: this op is rendered once for an output the setup presents
    /// and once more for this window, because the resolution preset here doesn't accept the presented size.</summary>
    private static void DrawRenderedTwiceWarning(ImDrawListPtr drawList, Vector2 captionPos)
    {
        var scale = T3Ui.UiScaleFactor;
        var iconSize = new Vector2(16, 16) * scale;
        var iconPos = captionPos - new Vector2(iconSize.X + 6 * scale, 2 * scale);
        Icons.DrawIconAtScreenPosition(Icon.Warning, iconPos, iconSize, drawList, UiColors.StatusAttention);

        if (ImGui.IsMouseHoveringRect(iconPos, iconPos + iconSize))
        {
            ImGui.SetTooltip("Rendered twice: once for the output setup, once more at this window's resolution.\n"
                             + "Set the resolution preset to Fill, or to the presented size, to show the presented frame instead.");
        }
    }

    public void SetViewMode(Modes newMode)
    {
        _viewMode = newMode;
        switch (newMode)
        {
            case Modes.Pixel:
                SetScaleToMatchPixels();
                // UserZoomedView = false;
                break;
            case Modes.Fitted:
                // UserZoomedView = false;
                // UserPannedView = false;
                break;

        }
    }


    /// <summary>
    /// Updated the view mode if user interacted 
    /// </summary>
    private void UpdateViewMode(InteractionState interactionState)
    {
        // UserZoomedView |= interactionState.UserZoomedCanvas;
        // UserPannedView |= interactionState.UserPannedCanvas;
            
        switch (_viewMode)
        {
            case Modes.Fitted:
                if (interactionState.UserPannedCanvas || interactionState.UserZoomedCanvas)
                {
                    _viewMode = Modes.Custom;
                }
                break;

            case Modes.Pixel:
                if (interactionState.UserZoomedCanvas)
                    _viewMode = Modes.Custom;
                break;
        }
    }


    private bool _isInteractionBlocked;

    public enum Modes
    {
        Fitted,
        Pixel,
        Custom,
    }

    public Modes ViewMode => _viewMode;
    private Modes _viewMode = Modes.Fitted;
    public bool DisableDamping = false;
        
    // public bool UserPannedView;
    // public bool UserZoomedView; 
}