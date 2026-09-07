using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.UiHelpers;

internal static class CanvasHelpers
{
    public static ImRect GetSavedOrValidViewForComposition(Guid compositionSymbolChildId, IEnumerable<ISelectableCanvasObject> childUisValues)
    {
        var hasSavedView = UserSettings.Config.ViewedCanvasAreaForSymbolChildId.TryGetValue(compositionSymbolChildId, out var newView);
        
        // Compare to selectable content
        var contentBounds = new ImRect();
        
        var isFirst = true;
        var requestedViewEmpty = true;
        foreach (var x in childUisValues)
        {
            var itemArea = ImRect.RectWithSize(x.PosOnCanvas, x.Size);
            if (newView.Contains(itemArea))
            {
                requestedViewEmpty = false;
            }

            if (isFirst)
            {
                contentBounds = itemArea;
                isFirst = false;
            }
            else
            {
                contentBounds.Add(itemArea);
            }
        }

        if (hasSavedView && !requestedViewEmpty) 
            return newView;
        
        contentBounds.Expand(400);
        newView = contentBounds;
        return newView;
    }
}