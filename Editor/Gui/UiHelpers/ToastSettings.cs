#nullable enable
namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// What the user has seen and learned from <see cref="ActionToast"/> messages. Tips are identified by stable string
/// ids, so features can add tips without changing the settings and old ids simply stay unused.
/// </summary>
public sealed class ToastSettings
{
    public bool Enabled = true;

    /** Tips whose feature has been used, so they aren't shown anymore */
    public readonly HashSet<string> LearnedTips = [];

    /** How often each tip was shown, so tips stop after a few times even if their feature isn't used */
    public readonly Dictionary<string, int> TipShowCounts = new();

    /** Actions whose notifications the user turned off */
    public readonly HashSet<string> MutedActions = [];
}
