namespace T3.Core.Audio.Native;

/// <summary>
/// A source or bus in the native audio engine. The default value is invalid, and a handle kept past its
/// release fails instead of reaching a later source or bus in the same slot.
/// </summary>
public readonly record struct AudioHandle(int Value)
{
    /// <summary>Routes to the device output, and meters what reaches it.</summary>
    public static readonly AudioHandle Output = new(0x7FFFFFFF);

    /** The native library returns a negative error code from create and load calls and never hands out 0. */
    public bool IsValid => Value > 0;
}
