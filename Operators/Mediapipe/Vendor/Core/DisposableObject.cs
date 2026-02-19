/// based on [OpenCvSharp](https://github.com/shimat/opencvsharp/blob/9a5f9828a74cfa3995562a06716e177705cde038/src/OpenCvSharp/Fundamentals/DisposableObject.cs)

namespace Mediapipe.Core;

public abstract class DisposableObject(bool isOwner) : IDisposable
{
    private volatile int _disposeSignaled;
    private bool _isLocked;

    protected DisposableObject() : this(true)
    {
    }

    public bool IsDisposed { get; protected set; }
    protected bool IsOwner { get; private set; } = isOwner;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_isLocked) throw new InvalidOperationException("Cannot dispose a locked object, unlock it first");

        if (Interlocked.Exchange(ref _disposeSignaled, 1) != 0) return;

        IsDisposed = true;

        if (disposing) DisposeManaged();
        DisposeUnmanaged();
    }

    ~DisposableObject()
    {
        Dispose(false);
    }

    protected virtual void DisposeManaged()
    {
    }

    protected virtual void DisposeUnmanaged()
    {
    }

    /// <summary>
    ///     Lock the object to prevent it from being disposed.
    /// </summary>
    internal void Lock()
    {
        _isLocked = true;
    }

    /// <summary>
    ///     Unlock the object to allow it to be disposed.
    /// </summary>
    internal void Unlock()
    {
        _isLocked = false;
    }

    /// <summary>Relinquish the ownership</summary>
    protected void TransferOwnership()
    {
        IsOwner = false;
    }

    protected void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(GetType().FullName);
    }
}