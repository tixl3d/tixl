#nullable enable
using ImGuiNET;
using T3.Core.Utils;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Gui.Interaction;

internal static class ImageBackgroundFading
{
    /// <summary>
    /// Computes fading out the image background below graph, when mouse pointer approaches left window border 
    /// </summary>
    public static void HandleImageBackgroundFading(GraphImageBackground imageBackground, out float backgroundImageOpacity)
    {
        backgroundImageOpacity = ComputeFadeFactor(ImGui.GetMousePos().X- ImGui.GetWindowPos().X);

        if (!imageBackground.IsActive || backgroundImageOpacity != 0) 
            return;
        
        if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            //imageBackground.ClearBackground();
        }
    }

    private const float ActiveBorderWidth = 50;
    private const float FadeOutWidth = 100;

    private static float ComputeFadeFactor(float distanceFromEdge)
    {
        return distanceFromEdge.RemapAndClamp(ActiveBorderWidth, ActiveBorderWidth + FadeOutWidth, 0, 1);
    }
    
    public static void HandleGraphFading(GraphImageBackground imageBackground, ImDrawListPtr drawList, out float graphOpacity)
    {
        graphOpacity = 1;
        if (!imageBackground.IsActive)
        {
            return;
        }

        if (!ImGui.IsWindowHovered())
            return;
        
        var windowPos = Vector2.Zero;
        var appWindowSize = ImGui.GetIO().DisplaySize;
        
        var mouseDistanceFromEdge = appWindowSize.X - ImGui.GetMousePos().X;
        graphOpacity = ComputeFadeFactor(mouseDistanceFromEdge); 
        
        if (graphOpacity < 1)
        {
            var x = windowPos.X + appWindowSize.X - ActiveBorderWidth;
            drawList.AddRectFilled(new Vector2(x, windowPos.Y),
                                   new Vector2(x + 1, windowPos.Y + appWindowSize.Y),
                                   UiColors.BackgroundFull.Fade((1 - graphOpacity)) * 0.5f);
            drawList.AddRectFilled(new Vector2(x + 1, windowPos.Y),
                                   new Vector2(x + 2, windowPos.Y + appWindowSize.Y),
                                   UiColors.ForegroundFull.Fade((1 - graphOpacity)) * 0.5f);
        }
        
        if (graphOpacity <= 0 && !ImGui.IsAnyItemActive() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            imageBackground.HasInteractionFocus = !imageBackground.HasInteractionFocus;
        }

        if (imageBackground.HasInteractionFocus)
        {
            graphOpacity *= 0.15f;
        }
    }
}