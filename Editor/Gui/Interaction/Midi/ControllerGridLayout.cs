#nullable enable
namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Maps a screen grid cell to a snapshot activation index for the snapshot control view's controller
/// grid. Indices are canonical — 0-based, with 0 at the top-left, reading order — and each controller
/// maps that index onto its own physical pads internally.
/// </summary>
public sealed class ControllerGridLayout(string name, int columns, int rows, Func<int, int, int> cellToIndex)
{
    public string Name { get; } = name;
    public int Columns { get; } = columns;
    public int Rows { get; } = rows;

    /// <summary>Activation index for the cell at (row from top, column from left).</summary>
    public int CellToIndex(int row, int column) => cellToIndex(row, column);
}

public static class ControllerGridLayouts
{
    /// <summary>Canonical top-down reading order — index 0 at the top-left.</summary>
    public static readonly ControllerGridLayout ReadingOrder
        = new("Reading order", 8, 8, (row, column) => row * 8 + column);
}
