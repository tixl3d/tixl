using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace T3.Graphics.Compat;

/// <summary>
/// A block of unmanaged memory with SharpDX's <c>DataStream</c> shape, so the upload and readback call sites
/// migrate unchanged. It either owns memory it allocated or borrows a pointer from a mapped resource.
/// </summary>
/// <remarks>
/// Prefer a span: <c>ResourceUtils</c> avoids the stream-returning overloads on purpose, because allocating
/// one per call dominated the profile in loops. This exists for the call sites that still hand a stream
/// around, not as the recommended path.
/// </remarks>
public sealed unsafe class DataStream : Stream
{
    /// <summary>Allocates its own memory.</summary>
    public DataStream(int sizeInBytes, bool canRead, bool canWrite)
    {
        _pointer = (byte*)NativeMemory.AlignedAlloc((nuint)sizeInBytes, 16);
        _length = sizeInBytes;
        _ownsMemory = true;
        _canRead = canRead;
        _canWrite = canWrite;
    }

    /// <summary>Borrows memory that belongs to someone else, usually a mapped resource.</summary>
    public DataStream(IntPtr dataPointer, long sizeInBytes, bool canRead, bool canWrite)
    {
        _pointer = (byte*)dataPointer;
        _length = sizeInBytes;
        _ownsMemory = false;
        _canRead = canRead;
        _canWrite = canWrite;
    }

    public IntPtr DataPointer => (IntPtr)_pointer;

    public IntPtr PositionPointer => (IntPtr)(_pointer + _position);

    public override bool CanRead => _canRead;
    public override bool CanWrite => _canWrite;
    public override bool CanSeek => true;
    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => _position = Math.Clamp(value, 0, _length);
    }

    public void Write<T>(T value) where T : struct
    {
        var size = Unsafe.SizeOf<T>();
        EnsureRoom(size);
        Unsafe.WriteUnaligned(_pointer + _position, value);
        _position += size;
    }

    public void WriteRange<T>(T[] data) where T : struct => WriteRange(data, 0, data.Length);

    public void WriteRange<T>(T[] data, int offset, int count) where T : struct
    {
        var span = new ReadOnlySpan<T>(data, offset, count);
        var bytes = MemoryMarshal.AsBytes(span);
        EnsureRoom(bytes.Length);
        bytes.CopyTo(new Span<byte>(_pointer + _position, bytes.Length));
        _position += bytes.Length;
    }

    public void WriteRange(IntPtr source, long byteCount)
    {
        EnsureRoom(byteCount);
        NativeMemory.Copy((void*)source, _pointer + _position, (nuint)byteCount);
        _position += byteCount;
    }

    public T Read<T>() where T : struct
    {
        var value = Unsafe.ReadUnaligned<T>(ref *(_pointer + _position));
        _position += Unsafe.SizeOf<T>();
        return value;
    }

    public T[] ReadRange<T>(int count) where T : struct
    {
        var result = new T[count];
        var bytes = MemoryMarshal.AsBytes<T>(result.AsSpan());
        new ReadOnlySpan<byte>(_pointer + _position, bytes.Length).CopyTo(bytes);
        _position += bytes.Length;
        return result;
    }

    public void ReadRange<T>(T[] buffer, int offset, int count) where T : struct
    {
        var bytes = MemoryMarshal.AsBytes(buffer.AsSpan(offset, count));
        new ReadOnlySpan<byte>(_pointer + _position, bytes.Length).CopyTo(bytes);
        _position += bytes.Length;
    }

    /// <summary>Reads into unmanaged memory at the current position.</summary>
    public void ReadRange(IntPtr destination, int byteCount)
    {
        NativeMemory.Copy(_pointer + _position, (void*)destination, (nuint)byteCount);
        _position += byteCount;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var available = (int)Math.Min(count, _length - _position);

        if (available <= 0)
            return 0;

        new ReadOnlySpan<byte>(_pointer + _position, available).CopyTo(buffer.AsSpan(offset, available));
        _position += available;
        return available;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        EnsureRoom(count);
        buffer.AsSpan(offset, count).CopyTo(new Span<byte>(_pointer + _position, count));
        _position += count;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        Position = origin switch
                       {
                           SeekOrigin.Current => _position + offset,
                           SeekOrigin.End     => _length + offset,
                           _                  => offset,
                       };

        return _position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) => throw new NotSupportedException("A DataStream has the length it was created with.");

    protected override void Dispose(bool disposing)
    {
        if (_ownsMemory && _pointer != null)
        {
            NativeMemory.AlignedFree(_pointer);
            _pointer = null;
        }

        base.Dispose(disposing);
    }

    private void EnsureRoom(long size)
    {
        if (_position + size > _length)
            throw new InvalidOperationException($"Writing {size} bytes at {_position} would run past the end of a {_length} byte stream.");
    }

    private byte* _pointer;
    private readonly long _length;
    private readonly bool _ownsMemory;
    private readonly bool _canRead;
    private readonly bool _canWrite;
    private long _position;
}

/// <summary>
/// The handful of helpers TiXL used from <c>SharpDX.Utilities</c>. Named apart from Core's own Utilities,
/// which operator projects import globally.
/// </summary>
public static unsafe class GraphicsUtilities
{
    public static void CopyMemory(IntPtr destination, IntPtr source, int sizeInBytes)
        => NativeMemory.Copy((void*)source, (void*)destination, (nuint)sizeInBytes);

    public static IntPtr AllocateMemory(int sizeInBytes, int align = 16) => (IntPtr)NativeMemory.AlignedAlloc((nuint)sizeInBytes, (nuint)align);

    public static void FreeMemory(IntPtr pointer) => NativeMemory.AlignedFree((void*)pointer);

    /// <summary>Copies out of unmanaged memory into an array, as SharpDX's Read did.</summary>
    public static void Read<T>(IntPtr source, T[] destination, int offset, int count) where T : struct
    {
        var bytes = MemoryMarshal.AsBytes(destination.AsSpan(offset, count));
        new ReadOnlySpan<byte>((void*)source, bytes.Length).CopyTo(bytes);
    }

    /// <summary>Disposes and nulls in one step, the way the operator code already writes it.</summary>
    public static void Dispose<T>(ref T? disposable) where T : class, IDisposable
    {
        disposable?.Dispose();
        disposable = null;
    }
}
