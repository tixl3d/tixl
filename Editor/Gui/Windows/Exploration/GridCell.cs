using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.Windows.Exploration;

public struct GridCell
{
    public int X;
    public int Y;

    public GridCell(int x, int y)
    {
        X = x;
        Y = y;
    }

    public static GridCell operator +(GridCell a, GridCell b)
    {
        return new GridCell(a.X + b.X, a.Y + b.Y);
    }

    public static GridCell operator +(GridCell a, Int2 b)
    {
        return new GridCell(a.X + b.Width, a.Y + b.Height);
    }

    public bool IsWithinGrid()
    {
        return X > 0 && X < VariationGridSize && Y > 0 && Y < VariationGridSize;
    }
            
    public static GridCell[] BuildSortedOffsets()
    {
        var offsets = new List<GridCell>();
        for (var x = -VariationGridSize; x < VariationGridSize; x++)
        {
            for (var y = -VariationGridSize; y < VariationGridSize; y++)
            {
                offsets.Add(new GridCell(x, y));
            }
        }

        offsets.Sort((a, b) => (a.X * a.X + a.Y * a.Y)
                               - (b.X * b.X + b.Y * b.Y));

        return offsets.ToArray();
    }


    public int GridIndex => Y * VariationGridSize + X;
    public static GridCell Center = new(VariationGridSize / 2, VariationGridSize / 2);
    public const int VariationGridSize = 100;
}