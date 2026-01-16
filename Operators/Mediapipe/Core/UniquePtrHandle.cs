namespace Mediapipe.Core;

public abstract class UniquePtrHandle(nint ptr, bool isOwner = true) : MpResourceHandle(ptr, isOwner)
{
    /// <summary>
    ///     The owning pointer
    /// </summary>
    /// <returns></returns>
    public abstract nint Get();

    /// <summary>
    ///     Release the owning pointer
    /// </summary>
    /// <returns></returns>
    public abstract nint Release();
}