#nullable enable
namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// A float that eases toward its target over a fixed duration with a power curve, advanced once per frame.
/// Transitions that share a duration can be compared by <see cref="Progress"/> to find the slowest one.
/// </summary>
internal struct EasedValue
{
    public float Target;
    public float From;

    /// <summary>0 at the start of a transition, 1 once settled.</summary>
    public float Progress;

    public float Value;

    public readonly bool IsSettled => Progress >= 1f;

    public static EasedValue Settled(float value)
    {
        return new EasedValue { Target = value, From = value, Progress = 1f, Value = value };
    }

    /// <summary>Starts easing from the current value toward <paramref name="target"/>; a no-op when already heading there.</summary>
    public bool Retarget(float target)
    {
        if (target == Target)
            return false;

        Target = target;
        From = Value;
        Progress = 0f;
        return true;
    }

    /// <summary>Begins a fresh 0 → 1 run — for eases whose value is a blend factor rather than a state.</summary>
    public void Restart()
    {
        From = 0f;
        Target = 1f;
        Value = 0f;
        Progress = 0f;
    }

    /// <summary>Lands on the target without animating.</summary>
    public void Settle()
    {
        Progress = 1f;
        Value = Target;
    }

    public void Advance(float dt, float durationSec, float exponent)
    {
        if (Progress >= 1f)
            return;

        Progress = MathF.Min(1f, Progress + dt / durationSec);
        var eased = MathF.Pow(Progress, exponent);
        Value = Progress >= 1f ? Target : From + (Target - From) * eased;
    }
}
