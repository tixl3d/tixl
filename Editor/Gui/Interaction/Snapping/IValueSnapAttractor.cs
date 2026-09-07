#nullable enable
namespace T3.Editor.Gui.Interaction.Snapping;

/// <summary>
/// Called by the SnapHandler to look for potential snap targets
/// </summary>
/// <remarks>should return null if not snapping</remarks>
internal interface IValueSnapAttractor
{
    void CheckForSnap(ref SnapResult result);
}