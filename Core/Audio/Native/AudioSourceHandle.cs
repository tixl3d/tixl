namespace T3.Core.Audio.Native;

/// <summary>
/// A sound in a <see cref="NativeAudioEngine"/>. The default value is invalid, and a handle kept past its
/// unload fails instead of reaching a later sound in the same slot.
/// </summary>
internal readonly record struct AudioSourceHandle(int Value)
{
    /** The native library returns a negative error code from load calls and never hands out 0. */
    public bool IsValid => Value > 0;
}
