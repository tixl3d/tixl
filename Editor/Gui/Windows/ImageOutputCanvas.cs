#nullable enable
using System.Diagnostics;
using ImGuiNET;
using SharpDX.DXGI;
using T3.Core.DataTypes;
using T3.Core.Resource;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.OutputUi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.SystemUi;

namespace T3.Editor.Gui.Windows;

internal sealed class ImageOutputCanvas : ScalableCanvas
{
    public void Update(T3Ui.EditingFlags editingFlags = T3Ui.EditingFlags.None)
    {
        UpdateCanvas(out var interactionState, editingFlags);
        UpdateViewMode(interactionState);
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

    public Texture2D? LastTexture;
        
    public void DrawTexture(Texture2D? texture)
    {
        CustomComponents.FillWithStripes(ImGui.GetWindowDrawList(), DrawUtils.GetContentRegionArea(), Scale.X);
        LastTexture = texture;
            
        Debug.Assert(Current != null);
        
        if (texture == null || texture.IsDisposed)
            return;

        var size = new Vector2(texture.Description.Width, texture.Description.Height);
        var area = new ImRect(0, 0, size.X, size.Y);

        if (_viewMode == Modes.Fitted)
        {
            Current.FitAreaOnCanvas(area);
            if (DisableDamping)
            {
                Scale = ScaleTarget;
            }                
        }

        var topLeft = Vector2.Zero;
        var topLeftOnScreen = Current.TransformPosition(topLeft);
        ImGui.SetCursorScreenPos(topLeftOnScreen);

        var sizeOnScreen = Current.TransformDirection(size);

        var srv = SrvManager.GetSrvForTexture(texture);
        ImGui.Image((IntPtr)srv, sizeOnScreen);

        if (ImGui.IsMouseHoveringRect(topLeftOnScreen, topLeftOnScreen + sizeOnScreen))
        {
            var relativePosition = (ImGui.GetMousePos() - topLeftOnScreen) / sizeOnScreen;
            MouseInput.Set(relativePosition, ImGui.IsMouseDown(ImGuiMouseButton.Left));
        }
            
        if (UserSettings.Config.ShowToolbar)
        {
            string format;
            if (srv == null || srv.IsDisposed)
            {
                format = "null?";
            } 
            else if (UserSettings.Config.ShowExplicitTextureFormatInOutputWindow)
            {
                format = srv.Description.Format.ToString();
            }
            else
            {
                switch (srv.Description.Format)
                {
                    case Format.R16G16B16A16_Float:
                        format = "RGBA:16";
                        break;
                    case Format.R8G8B8A8_SNorm:
                        format = "RGBA:8";
                        break;
                    default:
                        format = srv.Description.Format.ToString();
                        break;
                }
            }
                
            ImGui.PushFont(Fonts.FontSmall);
            var zoom = Math.Abs(Scale.X) < 0.001f ? "" : $" ×{Scale.X:G2}";
            var description = $"{size.X}x{size.Y}  {format} {zoom}";
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
        }
    }
        
    //private static readonly Color ShadowColor = new Color(0.0f, 0.0f, 0.0f, 0.6f);


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