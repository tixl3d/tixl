using System;
using System.IO;
using System.Runtime.InteropServices;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Streaming RIFF / WAVE writer for 16-bit PCM audio. Opens a file with a placeholder
/// header, accepts interleaved float32 sample buffers from a WASAPI capture callback,
/// converts to int16 PCM on the fly, and finalises the RIFF / data chunk sizes on
/// <see cref="Dispose"/>.
/// </summary>
/// <remarks>
/// <para>
/// Thread-safe: <see cref="AppendFloat32Samples"/> is called from the BASS WASAPI
/// capture callback while <see cref="Dispose"/> is typically called from the main thread.
/// A single lock guards both. The lock is uncontended in the common case (one writer per
/// recording session, one callback firing every ~10 ms).
/// </para>
/// <para>
/// Allocation behaviour: the internal conversion buffers grow once (the first time a
/// callback delivers a larger payload than the initial 4096-sample size) and then stay
/// resident for the lifetime of the writer. There are no allocations on the steady-state
/// hot path.
/// </para>
/// </remarks>
public sealed class WavFileWriter : IDisposable
{
    /// <summary>
    /// Opens <paramref name="path"/> for writing and emits a placeholder 44-byte WAV header.
    /// The header is rewritten with the correct sizes when the writer is disposed.
    /// </summary>
    /// <param name="path">Destination file path. Parent directories must exist.</param>
    /// <param name="sampleRate">Sample rate of the incoming float32 samples (e.g. 48000).</param>
    /// <param name="channels">Number of interleaved channels (1 = mono, 2 = stereo).</param>
    /// <exception cref="ArgumentOutOfRangeException">Invalid sample rate or channel count.</exception>
    public WavFileWriter(string path, int sampleRate, int channels)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");

        if (channels <= 0 || channels > 8)
            throw new ArgumentOutOfRangeException(nameof(channels), channels, "Channel count must be 1..8.");

        Path = path;
        SampleRate = sampleRate;
        Channels = channels;

        _stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        WritePlaceholderHeader();
    }

    public string Path { get; }
    public int SampleRate { get; }
    public int Channels { get; }

    /// <summary>Total PCM sample bytes written so far (header bytes excluded).</summary>
    public long BytesWritten => _bytesWritten;

    /// <summary>Duration in seconds of audio written so far.</summary>
    public double DurationSeconds => _bytesWritten / (double)(SampleRate * Channels * BytesPerSample);

    /// <summary>
    /// Appends a buffer of interleaved float32 samples to the WAV file.
    /// Samples are clamped to [-1, 1] and converted to 16-bit PCM little-endian.
    /// </summary>
    /// <param name="buffer">Pointer to the source buffer (BASS callback's <c>buffer</c>).</param>
    /// <param name="byteCount">Number of bytes available at <paramref name="buffer"/>.</param>
    public void AppendFloat32Samples(IntPtr buffer, int byteCount)
    {
        if (byteCount <= 0 || buffer == IntPtr.Zero)
            return;

        var floatCount = byteCount / 4;
        var pcmByteCount = floatCount * BytesPerSample;

        lock (_lock)
        {
            if (_disposed)
                return;

            EnsureBufferCapacity(floatCount);

            Marshal.Copy(buffer, _floatBuffer, 0, floatCount);

            for (var i = 0; i < floatCount; i++)
            {
                var sample = _floatBuffer[i];
                if (sample > 1f)
                    sample = 1f;
                else if (sample < -1f)
                    sample = -1f;

                _shortBuffer[i] = (short)(sample * 32767f);
            }

            Buffer.BlockCopy(_shortBuffer, 0, _byteBuffer, 0, pcmByteCount);

            try
            {
                _stream.Write(_byteBuffer, 0, pcmByteCount);
                _bytesWritten += pcmByteCount;
            }
            catch (Exception e)
            {
                Log.Warning($"WavFileWriter: write failed for '{Path}' - {e.Message}");
            }
        }
    }

    /// <summary>
    /// Closes the file and rewrites the RIFF / data chunk sizes in the header.
    /// Safe to call multiple times.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
                return;

            _disposed = true;

            try
            {
                FinaliseHeader();
            }
            catch (Exception e)
            {
                Log.Warning($"WavFileWriter: header finalise failed for '{Path}' - {e.Message}. File may be unreadable.");
            }
            finally
            {
                _stream.Dispose();
            }
        }
    }

    private void EnsureBufferCapacity(int floatCount)
    {
        if (_floatBuffer.Length < floatCount)
            _floatBuffer = new float[floatCount];

        if (_shortBuffer.Length < floatCount)
            _shortBuffer = new short[floatCount];

        var pcmByteCount = floatCount * BytesPerSample;
        if (_byteBuffer.Length < pcmByteCount)
            _byteBuffer = new byte[pcmByteCount];
    }

    private void WritePlaceholderHeader()
    {
        var byteRate = SampleRate * Channels * BytesPerSample;
        var blockAlign = (short)(Channels * BytesPerSample);

        // RIFF header
        WriteAscii("RIFF");
        WriteInt32(0);              // placeholder: RIFF chunk size (file size - 8)
        WriteAscii("WAVE");

        // fmt chunk
        WriteAscii("fmt ");
        WriteInt32(16);             // fmt chunk size for PCM
        WriteInt16(1);              // audio format: 1 = PCM
        WriteInt16((short)Channels);
        WriteInt32(SampleRate);
        WriteInt32(byteRate);
        WriteInt16(blockAlign);
        WriteInt16(16);             // bits per sample

        // data chunk header
        WriteAscii("data");
        WriteInt32(0);              // placeholder: data chunk size
    }

    private void FinaliseHeader()
    {
        var dataChunkSize = (int)Math.Min(_bytesWritten, int.MaxValue);
        var riffChunkSize = dataChunkSize + 36;  // header bytes after the RIFF size field

        _stream.Flush();
        _stream.Seek(4, SeekOrigin.Begin);
        WriteInt32(riffChunkSize);

        _stream.Seek(40, SeekOrigin.Begin);
        WriteInt32(dataChunkSize);
        _stream.Flush();
    }

    private void WriteAscii(string ascii)
    {
        for (var i = 0; i < ascii.Length; i++)
            _stream.WriteByte((byte)ascii[i]);
    }

    private void WriteInt32(int value)
    {
        _stream.WriteByte((byte)(value & 0xff));
        _stream.WriteByte((byte)((value >> 8) & 0xff));
        _stream.WriteByte((byte)((value >> 16) & 0xff));
        _stream.WriteByte((byte)((value >> 24) & 0xff));
    }

    private void WriteInt16(short value)
    {
        _stream.WriteByte((byte)(value & 0xff));
        _stream.WriteByte((byte)((value >> 8) & 0xff));
    }

    private const int BytesPerSample = 2; // 16-bit PCM
    private const int InitialSampleCapacity = 4096;

    private readonly object _lock = new();
    private readonly FileStream _stream;
    private long _bytesWritten;
    private bool _disposed;

    private float[] _floatBuffer = new float[InitialSampleCapacity];
    private short[] _shortBuffer = new short[InitialSampleCapacity];
    private byte[] _byteBuffer = new byte[InitialSampleCapacity * BytesPerSample];
}
