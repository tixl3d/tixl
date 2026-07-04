#nullable enable
using T3.Editor.Gui.Interaction.Snapping;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.MagGraph.Model;

/// <summary>
/// A wrapper to <see cref="Section"/> to provide damping other potential other features of the mag graph UI.
/// </summary>
internal sealed class MagGraphSection : ISelectableCanvasObject, IValueSnapAttractor
{
    public required Section Section;
    public ISelectableCanvasObject Selectable => Section;
    
    public Vector2 PosOnCanvas { get => Section.PosOnCanvas; set => Section.PosOnCanvas = value; }
    public Vector2 Size { get => Section.Size; set => Section.Size = value; }

    public Vector2 DampedPosOnCanvas;
    public Vector2 DampedSize;
    
    public Guid Id { get; init; }
    
    public int LastUpdateCycle;
    public bool IsRemoved;

    void IValueSnapAttractor.CheckForSnap(ref SnapResult snapResult)
    {
        if (snapResult.Orientation == SnapResult.Orientations.Horizontal)
        {
            snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.X);
            snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.X + DampedSize.X);
        }
        else  if (snapResult.Orientation == SnapResult.Orientations.Vertical)
        {
            snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.Y);
            snapResult.TryToImproveWithAnchorValue(DampedPosOnCanvas.Y + DampedSize.Y);
        }
    }
}