
#nullable enable
namespace T3.Editor.Gui.Interaction;

internal sealed class CurrentGraphSubCanvas : ScalableCanvas
{
    public CurrentGraphSubCanvas(Vector2? initialScale = null) : base(initialScale) { }
    protected override ScalableCanvas? Parent => null; //ProjectView.Focused?.GraphCanvas;
}