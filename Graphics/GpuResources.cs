namespace T3.Graphics;

/// <summary>
/// Base for everything a backend hands out. Disposal is deferred: the backend releases the resource once the
/// frames that may still reference it have retired, which is what lets operators dispose and recreate views
/// mid-frame the way they do under D3D11.
/// </summary>
public abstract class GpuResource : IDisposable
{
    protected GpuResource(string? label)
    {
        Label = label;
    }

    /// <summary>Shows up in RenderDoc and in validation messages. D3D11 calls this DebugName.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// True once <see cref="Dispose"/> was called. Operator code polls this today because a resource can be
    /// released by an upstream operator between frames, and binding a dead resource has to stay harmless.
    /// </summary>
    public bool IsDisposed { get; private set; }

    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;
        ReleaseWhenRetired();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The native handle, where one exists: a COM pointer on D3D11, which the video and capture libraries
    /// take. Zero everywhere else.
    /// </summary>
    public virtual IntPtr NativeHandle => IntPtr.Zero;

    /// <summary>Hands the native object to the backend's deferred-destruction queue.</summary>
    protected abstract void ReleaseWhenRetired();
}

/// <summary>A texture of any dimension; the description says which.</summary>
public abstract class GpuTexture(TextureDescription description, string? label) : GpuResource(label)
{
    /// <summary>A snapshot, as in TiXL's current wrappers: call sites read Width/Height/Format constantly.</summary>
    public readonly TextureDescription Description = description;
}

/// <summary>A view of part of a texture, and what a shader binding or a render target actually points at.</summary>
public abstract class GpuTextureView(GpuTexture texture, TextureViewDescription description, string? label) : GpuResource(label)
{
    public readonly GpuTexture Texture = texture;
    public readonly TextureViewDescription Description = description;

    /// <summary>
    /// The id ImGui draws with. Replaces passing a native pointer around: the renderer resolves it at draw
    /// time, and a view that died in between draws nothing instead of crashing.
    /// </summary>
    public abstract ulong ImGuiTextureId { get; }
}

public abstract class GpuBuffer(GpuBufferDescription description, string? label) : GpuResource(label)
{
    public readonly GpuBufferDescription Description = description;
}

public abstract class GpuSampler(SamplerDescription description, string? label) : GpuResource(label)
{
    public readonly SamplerDescription Description = description;
}

/// <summary>A compiled shader stage. Created by the shader compiler, never by operators.</summary>
public abstract class GpuShader(ShaderStage stage, string? label) : GpuResource(label)
{
    public readonly ShaderStage Stage = stage;
}

/// <summary>
/// A complete graphics or compute pipeline. D3D11's mutable state has no equivalent, so the compatibility
/// layer hashes its current state into one of these; the backend caches them by description.
/// </summary>
public abstract class GpuPipeline(string? label) : GpuResource(label);

public enum ShaderStage
{
    Vertex,
    Pixel,
    Geometry,
    Compute,
}
