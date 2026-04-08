#nullable enable
using System;
using System.Diagnostics;
using T3.Core.DataTypes.Vector;

#if PLATFORM_WINDOWS
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;

namespace T3.Core.DataTypes;

// for some bytecode access convenience (reflection) and to avoid direct SharpDX references for later
public sealed class ComputeShader(SharpDX.Direct3D11.ComputeShader shader, byte[] compiledBytecode)
    : Shader<SharpDX.Direct3D11.ComputeShader>(shader, compiledBytecode)
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

public sealed class PixelShader(SharpDX.Direct3D11.PixelShader shader, byte[] compiledBytecode)
    : Shader<SharpDX.Direct3D11.PixelShader>(shader, compiledBytecode);

public sealed class VertexShader(SharpDX.Direct3D11.VertexShader shader, byte[] compiledBytecode)
    : Shader<SharpDX.Direct3D11.VertexShader>(shader, compiledBytecode);

public sealed class GeometryShader(SharpDX.Direct3D11.GeometryShader shader, byte[] compiledBytecode)
    : Shader<SharpDX.Direct3D11.GeometryShader>(shader, compiledBytecode);

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

#else // !PLATFORM_WINDOWS -- Linux stubs

namespace T3.Core.DataTypes;

public sealed class ComputeShader : AbstractShader
{
    public bool TryGetThreadGroups(out Int3 threadGroups)
    {
        threadGroups = default;
        return false;
    }
}

public sealed class PixelShader : AbstractShader;
public sealed class VertexShader : AbstractShader;
public sealed class GeometryShader : AbstractShader;

public abstract class AbstractShader : IDisposable
{
    internal byte[] CompiledBytecode = [];
    public virtual string Name { get; set; } = string.Empty;
    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        GC.SuppressFinalize(this);
    }

    ~AbstractShader() => Dispose();
}

#endif
