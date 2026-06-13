#nullable enable
using System.Reflection;

namespace T3.Editor.Gui.Interaction.Midi;

/// <summary>
/// Maps a screen grid cell to a snapshot activation index, so the snapshot control view can show
/// the index field in the same arrangement as a physical controller (e.g. the APC Mini's bottom-up
/// 8×8). Controllers expose their own layout (they already know this mapping to drive LED colors);
/// a built-in "Reading order" is the default for users without a launchpad.
/// </summary>
public sealed class ControllerGridLayout(string name, int columns, int rows, Func<int, int, int> cellToIndex)
{
    public string Name { get; } = name;
    public int Columns { get; } = columns;
    public int Rows { get; } = rows;

    /// <summary>1-based activation index for the cell at (row from top, column from left).</summary>
    public int CellToIndex(int row, int column) => cellToIndex(row, column);
}

/// <summary>
/// All grid layouts offered in the snapshot control view's controller grid: the built-in reading
/// order plus whatever the known <see cref="CompatibleMidiDevice"/>s expose via
/// <see cref="CompatibleMidiDevice.GridLayout"/>.
/// </summary>
public static class ControllerGridLayouts
{
    /// <summary>Natural top-down reading order — index 1 at the top-left.</summary>
    public static readonly ControllerGridLayout ReadingOrder
        = new("Reading order", 8, 8, (row, column) => row * 8 + column + 1);

    public static IReadOnlyList<ControllerGridLayout> All => _all ??= Collect();

    private static List<ControllerGridLayout> Collect()
    {
        var layouts = new List<ControllerGridLayout> { ReadingOrder };

        var baseType = typeof(CompatibleMidiDevice);
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (type.IsAbstract || !baseType.IsAssignableFrom(type))
                continue;

            try
            {
                if (Activator.CreateInstance(type) is CompatibleMidiDevice device && device.GridLayout is { } layout)
                    layouts.Add(layout);
            }
            catch (Exception e)
            {
                Log.Warning($"Failed to read grid layout from {type.Name}: {e.Message}");
            }
        }

        return layouts;
    }

    private static List<ControllerGridLayout>? _all;
}
