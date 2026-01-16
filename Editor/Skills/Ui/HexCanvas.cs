#nullable enable
using T3.Editor.Gui.Interaction;

namespace T3.Editor.Skills.Ui;

public class HexCanvas : ScalableCanvas
{
    internal float GridSize = 100;

    internal float HexRadiusOnScreen => GridSize * Scale.X;

    public struct Cell : IEquatable<Cell>
    {
        internal Cell(int x, int y)
        {
            X = x;
            Y = y;
        }

        internal Cell(Vector2 vec)
        {
            X = (int)vec.X;
            Y = (int)vec.Y;
        }

        internal int X;
        internal int Y;
        
        public static bool operator ==(Cell left, Cell right)
        {
            return left.X == right.X && left.Y == right.Y;
        }
        
        public static Cell operator +(Cell left, Cell delta)
        {
            return new Cell(left.X + delta.X, left.Y + delta.Y);
        }
        
        
        public static bool operator !=(Cell left, Cell right)
        {
            return !(left == right);
        }

        public bool Equals(Cell other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object? obj)
        {
            return obj is Cell other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Y * 16384 + X;
        }
    }

    internal Cell CellFromScreenPos(Vector2 screenPos)
    {
        var posOnCanvas = InverseTransformPositionFloat(screenPos);
        return CellFromCanvasPos(posOnCanvas);
    }

    internal Cell CellFromCanvasPos(Vector2 canvasPos)
    {
        const float sqrt3 = 1.7320508075688772f;

        var x = canvasPos.X;
        var y = canvasPos.Y;

        var fq = (sqrt3 / 3f * x - 1f / 3f * y) / GridSize;
        var fr = (2f / 3f * y) / GridSize;

        var (q, row) = AxialRound(fq, fr);

        // 2) axial (q, r) -> odd-r row-offset (X = column, Y = row)
        // rows with (Y % 2 == 1) are shifted by half a hex width
        var col = q; //  (row - (row & 1)) / 2; // odd-r

        return new Cell(col, row);
    }

    internal Vector2 ScreenPosFromCell(Cell cell)
    {
        var canvasPos = CanvasPosFromCell(cell);
        return TransformPositionFloat(canvasPos);
    }

    /// <summary>
    /// To simplify serialization, we can store cells as Vec2.
    /// </summary>
    internal Vector2 MapCoordsToScreenPos(Vector2 mapCoords)
    {
        var canvasPos = CanvasPosFromCell(new Cell((int)mapCoords.X, (int)mapCoords.Y));
        return TransformPositionFloat(canvasPos);
    }

    internal Vector2 CanvasPosFromCell(Cell cell)
    {
        const float sqrt3 = 1.7320508075688772f;

        var col = cell.X;
        var row = cell.Y;

        // odd-r offset -> axial (q, r)
        // rows with (row % 2 == 1) are shifted by half a hex width
        var q = col; //  (row - (row & 1)) / 2;
        var r = row;

        // axial (q, r) -> canvas position of the *cell center*
        var x = GridSize * (sqrt3 * q + sqrt3 / 2f * r);
        var y = GridSize * (3f / 2f * r);

        return new Vector2(x, y);
    }

    // Rounds fractional axial coords to the nearest hex using cube-coord rounding
    private static (int q, int r) AxialRound(float fq, float fr)
    {
        // axial (q, r) -> cube (x, y, z)
        var fx = (double)fq;
        var fz = (double)fr;
        var fy = -fx - fz;

        var rx = (int)Math.Round(fx);
        var ry = (int)Math.Round(fy);
        var rz = (int)Math.Round(fz);

        var xDiff = Math.Abs(rx - fx);
        var yDiff = Math.Abs(ry - fy);
        var zDiff = Math.Abs(rz - fz);

        if (xDiff > yDiff && xDiff > zDiff)
        {
            rx = -ry - rz;
        }
        else if (yDiff > zDiff)
        {
            ry = -rx - rz;
        }
        else
        {
            rz = -rx - ry;
        }

        // back to axial (q, r)
        return (rx, rz);
    }
}