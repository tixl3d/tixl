namespace Mediapipe.Core;

public abstract class SharedPtrHandle(nint ptr, bool isOwner = true) : MpResourceHandle(ptr, isOwner)
{
    /// <summary>
    ///     The owning pointer
    /// </summary>
    /// <returns></returns>
    public abstract nint Get();

    /// <summary>
    ///     Release the owning pointer
    /// </summary>
    public abstract void Reset();
}