using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace T3.Core.Resource.Dds;

/// <summary>
/// Specifies data for initializing a subresource.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly struct D3D11SubResourceData : IEquatable<D3D11SubResourceData>
{
    /// <summary>
    /// The initialization data.
    /// </summary>
    private readonly Array data;

    /// <summary>
    /// The distance (in bytes) from the beginning of one line of a texture to the next line.
    /// </summary>
    private readonly uint pitch;

    /// <summary>
    /// The distance (in bytes) from the beginning of one depth level to the next.
    /// </summary>
    private readonly uint slicePitch;

    /// <summary>
    /// Initializes a new instance of the <see cref="D3D11SubResourceData"/> struct.
    /// </summary>
    /// <param name="data">The initialization data.</param>
    /// <param name="pitch">The distance (in bytes) from the beginning of one line of a texture to the next line.</param>
    public D3D11SubResourceData(Array data, uint pitch)
    {
        this.data = data;
        this.pitch = pitch;
        slicePitch = 0;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="D3D11SubResourceData"/> struct.
    /// </summary>
    /// <param name="data">The initialization data.</param>
    /// <param name="pitch">The distance (in bytes) from the beginning of one line of a texture to the next line.</param>
    /// <param name="slicePitch">The distance (in bytes) from the beginning of one depth level to the next.</param>
    public D3D11SubResourceData(Array data, uint pitch, uint slicePitch)
    {
        this.data = data;
        this.pitch = pitch;
        this.slicePitch = slicePitch;
    }

    /// <summary>
    /// Gets the initialization data.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays", Justification = "Reviewed")]
    public Array Data { get { return data; } }

    /// <summary>
    /// Gets the distance (in bytes) from the beginning of one line of a texture to the next line.
    /// </summary>
    public uint Pitch { get { return pitch; } }

    /// <summary>
    /// Gets the distance (in bytes) from the beginning of one depth level to the next.
    /// </summary>
    public uint SlicePitch { get { return slicePitch; } }

    /// <summary>
    /// Compares two <see cref="D3D11SubResourceData"/> objects. The result specifies whether the values of the two objects are equal.
    /// </summary>
    /// <param name="left">The left <see cref="D3D11SubResourceData"/> to compare.</param>
    /// <param name="right">The right <see cref="D3D11SubResourceData"/> to compare.</param>
    /// <returns><value>true</value> if the values of left and right are equal; otherwise, <value>false</value>.</returns>
    public static bool operator ==(D3D11SubResourceData left, D3D11SubResourceData right)
    {
        return left.Equals(right);
    }

    /// <summary>
    /// Compares two <see cref="D3D11SubResourceData"/> objects. The result specifies whether the values of the two objects are unequal.
    /// </summary>
    /// <param name="left">The left <see cref="D3D11SubResourceData"/> to compare.</param>
    /// <param name="right">The right <see cref="D3D11SubResourceData"/> to compare.</param>
    /// <returns><value>true</value> if the values of left and right differ; otherwise, <value>false</value>.</returns>
    public static bool operator !=(D3D11SubResourceData left, D3D11SubResourceData right)
    {
        return !(left == right);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="obj">The object to compare with the current object.</param>
    /// <returns><value>true</value> if the specified object is equal to the current object; otherwise, <value>false</value>.</returns>
    public override bool Equals(object obj)
    {
        if (!(obj is D3D11SubResourceData))
        {
            return false;
        }

        return Equals((D3D11SubResourceData)obj);
    }

    /// <summary>
    /// Determines whether the specified object is equal to the current object.
    /// </summary>
    /// <param name="other">The object to compare with the current object.</param>
    /// <returns><value>true</value> if the specified object is equal to the current object; otherwise, <value>false</value>.</returns>
    public bool Equals(D3D11SubResourceData other)
    {
        return data == other.data
               && pitch == other.pitch
               && slicePitch == other.slicePitch;
    }

    /// <summary>
    /// Returns the hash code for this instance.
    /// </summary>
    /// <returns>A 32-bit signed integer that is the hash code for this instance.</returns>
    public override int GetHashCode()
    {
        return new
                   {
                       data,
                       pitch,
                       slicePitch
                   }
           .GetHashCode();
    }
}