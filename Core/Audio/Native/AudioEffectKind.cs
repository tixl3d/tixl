namespace T3.Core.Audio.Native;

/// <summary>The effect a bus runs on what flows through it. The values match the native library's.</summary>
public enum AudioEffectKind
{
    None = 0,
    Echo = 1,
    Compressor = 2,
    Reverb = 3,
    Filter = 4,
}
