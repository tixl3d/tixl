#nullable enable
using System;
using System.Diagnostics;
using SharpDX.D3DCompiler;
using T3.Graphics.Compat;
using T3.Core.DataTypes.Vector;

namespace T3.Core.DataTypes;

// for some bytecode access convenience (reflection) and to avoid direct SharpDX references for later
public sealed class ComputeShader(T3.Graphics.Compat.ComputeShader shader, byte[] compiledBytecode)
    : Shader<T3.Graphics.Compat.ComputeShader>(shader, compiledBytecode)
{
    public bool TryGetThreadGroups(out Int3 threadGroups)
    {
        threadGroups = default;

        using var reflection = new ShaderReflection(CompiledBytecode);
        _ = reflection.GetThreadGroupSize(out var x, out var y, out var z);

        threadGroups = new Int3(x, y, z);
        return true;
    }
}

public sealed class PixelShader(T3.Graphics.Compat.PixelShader shader, byte[] compiledBytecode)
    : Shader<T3.Graphics.Compat.PixelShader>(shader, compiledBytecode);

public sealed class VertexShader(T3.Graphics.Compat.VertexShader shader, byte[] compiledBytecode)
    : Shader<T3.Graphics.Compat.VertexShader>(shader, compiledBytecode);

public sealed class GeometryShader(T3.Graphics.Compat.GeometryShader shader, byte[] compiledBytecode)
    : Shader<T3.Graphics.Compat.GeometryShader>(shader, compiledBytecode);

public abstract class Shader<TShader> : AbstractShader where TShader : DeviceChild
{
    private readonly TShader _shader;
    public sealed override string Name { get => _shader.DebugName; set => _shader.DebugName = value; }

    public static implicit operator TShader?(Shader<TShader>? shader) => shader?._shader;

    internal Shader(TShader shader, byte[] compiledBytecode) : base(shader, compiledBytecode)
    {
        Debug.Assert(shader != null);
        _shader = shader;
    }
}

public abstract class AbstractShader : IDisposable
{
    internal readonly byte[] CompiledBytecode;
    private readonly IDisposable _shader;
    public abstract string Name { get; set; }
    private bool _disposed;

    internal AbstractShader(IDisposable shader, byte[] compiledBytecode)
    {
        CompiledBytecode = compiledBytecode;
        _shader = shader;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _shader.Dispose();
    }

    ~AbstractShader()
    {
        Dispose();
    }
}