using System;

namespace T3.Core.Utils.Math3d;

public static class MatrixExtensions
{
    public static Vector4 Row1(this Matrix4x4 mat) => new(mat.M11, mat.M12, mat.M13, mat.M14);

    public static Vector4 Row2(this Matrix4x4 mat) => new(mat.M21, mat.M22, mat.M23, mat.M24);

    public static Vector4 Row3(this Matrix4x4 mat) => new(mat.M31, mat.M32, mat.M33, mat.M34);

    public static Vector4 Row4(this Matrix4x4 mat) => new(mat.M41, mat.M42, mat.M43, mat.M44);

    /// <summary>
    /// Transposes the specified matrix. -Copied from SharpDX
    /// For use with row-major matrices (like DirectX matrices)
    /// </summary>
    /// <param name="mat"></param>
    public static void Transpose(ref this Matrix4x4 mat) => Transpose(ref mat, out mat);

    /// <summary>
    /// Calculates the transpose of the specified matrix. -Copied from SharpDX
    /// </summary>
    /// <param name="value">The matrix whose transpose is to be calculated.</param>
    /// <param name="result">When the method completes, contains the transpose of the specified matrix.</param>
    public static void Transpose(ref Matrix4x4 value, out Matrix4x4 result)
    {
        Matrix4x4 temp = new Matrix4x4();
        temp.M11 = value.M11;
        temp.M12 = value.M21;
        temp.M13 = value.M31;
        temp.M14 = value.M41;
        temp.M21 = value.M12;
        temp.M22 = value.M22;
        temp.M23 = value.M32;
        temp.M24 = value.M42;
        temp.M31 = value.M13;
        temp.M32 = value.M23;
        temp.M33 = value.M33;
        temp.M34 = value.M43;
        temp.M41 = value.M14;
        temp.M42 = value.M24;
        temp.M43 = value.M34;
        temp.M44 = value.M44;

        result = temp;
    }

    public static Matrix4x4 ToMatrixFromRows(this Vector4[] rows)
    {
        if (rows == null || rows.Length != 4)
        {
            Logging.Log.Error("Invalid number of rows for matrix " + Environment.StackTrace);
            return Matrix4x4.Identity;
        }

        return new Matrix4x4(
                             rows[0].X, rows[0].Y, rows[0].Z, rows[0].W,
                             rows[1].X, rows[1].Y, rows[1].Z, rows[1].W,
                             rows[2].X, rows[2].Y, rows[2].Z, rows[2].W,
                             rows[3].X, rows[3].Y, rows[3].Z, rows[3].W);
    }
    
    public static Quaternion GetRotation(this Matrix4x4 matrix) => Quaternion.CreateFromRotationMatrix(matrix);

    /// <summary>
    /// Gets or sets the translation of the matrix; that is M41, M42, and M43.
    /// </summary>
    public static Vector3 GetTranslationVector(this Matrix4x4 matrix) => new(matrix.M41, matrix.M42, matrix.M43);

    public static void SetTranslationVector(this Matrix4x4 matrix, Vector3 translation)
    {
        matrix.M41 = translation.X;
        matrix.M42 = translation.Y;
        matrix.M43 = translation.Z;
    }

    public static Vector3 GetScaleVector(this Matrix4x4 matrix) => new(matrix.M11, matrix.M22, matrix.M33);

    public static void SetScaleVector(this Matrix4x4 matrix, Vector3 scale)
    {
        matrix.M11 = scale.X;
        matrix.M22 = scale.Y;
        matrix.M33 = scale.Z;
    }
}