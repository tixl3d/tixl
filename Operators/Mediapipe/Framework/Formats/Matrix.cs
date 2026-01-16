using Mediapipe.TranMarshal;

namespace Mediapipe.Framework.Formats;

public readonly struct Matrix
{
    public enum Layout
    {
        ColMajor = 0,
        RowMajor = 1
    }

    internal readonly float[] data;
    public readonly int rows;
    public readonly int cols;
    internal readonly Layout layout;

    public Matrix(float[] data, int rows, int cols, Layout layout = Layout.ColMajor)
    {
        if (rows * cols != data.Length)
            throw new ArgumentException($"Matrix size mismatch ({rows}x{cols} != {data.Length})");

        this.data = data;
        this.rows = rows;
        this.cols = cols;
        this.layout = layout;
    }

    internal Matrix(NativeMatrix nativeMatrix) : this(nativeMatrix.AsReadOnlySpan().ToArray(), nativeMatrix.rows,
        nativeMatrix.cols, nativeMatrix.layout == 0 ? Layout.ColMajor : Layout.RowMajor)
    {
    }

    internal static void Copy(NativeMatrix source, ref Matrix destination)
    {
        if (destination.rows != source.rows || destination.cols != source.cols)
            throw new ArgumentException(
                $"Matrix size mismatch ({source.rows}x{source.cols} != {destination.rows}x{destination.cols})");

        source.AsReadOnlySpan().CopyTo(destination.data);
        Layout layout = source.layout == 0 ? Layout.ColMajor : Layout.RowMajor;

        destination = new Matrix(destination.data, source.rows, source.cols, layout);
    }

    public readonly bool IsColMajor => layout == Layout.ColMajor;
    public readonly bool IsRowMajor => layout == Layout.RowMajor;
}