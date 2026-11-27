using System;

namespace Lib.Utils.StraightSkeleton.Primitives
{
    /// <summary> Represents point or vector in 2D space. </summary>
    internal struct Vector2d
    {
        public static Vector2d Empty = new Vector2d(double.MinValue, double.MinValue);

        public double X;
        public double Y;

        public Vector2d(Vector2d var1)
        {
            X = var1.X;
            Y = var1.Y;
        }

        public Vector2d(double var1, double var3)
        {
            X = var1;
            Y = var3;
        }

        public void Negate()
        {
            X = -X;
            Y = -Y;
        }

        public double DistanceTo(Vector2d var1)
        {
            var var2 = X - var1.X;
            var var4 = Y - var1.Y;
            return Math.Sqrt(var2 * var2 + var4 * var4);
        }

        public Vector2d Normalized()
        {
            var var1 = 1.0D/Math.Sqrt(X*X + Y*Y);
            return new Vector2d(X *= var1, Y *= var1);
        }

        public double Dot(Vector2d var1)
        {
            return X*var1.X + Y*var1.Y;
        }

        public double DistanceSquared(Vector2d var1)
        {
            var var2 = X - var1.X;
            var var4 = Y - var1.Y;
            return var2*var2 + var4*var4;
        }

        public static Vector2d operator -(Vector2d left, Vector2d right)
        {
            return new Vector2d(left.X - right.X, left.Y - right.Y);
        }

        public static Vector2d operator +(Vector2d left, Vector2d right)
        {
            return new Vector2d(left.X + right.X, left.Y + right.Y);
        }

        public static Vector2d operator *(Vector2d left, double scale)
        {
            return new Vector2d(left.X * scale, left.Y * scale);
        }

        public static bool operator ==(Vector2d left, Vector2d right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Vector2d left, Vector2d right)
        {
            return !(left == right);
        }

        public bool Equals(Vector2d obj)
        {
            return X.Equals(obj.X) && Y.Equals(obj.Y);
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            return obj is Vector2d && Equals((Vector2d)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode()*397) ^ Y.GetHashCode();
            }
        }

        public override string ToString()
        {
            return string.Format("({0}, {1})", X, Y);
        }
    }
}