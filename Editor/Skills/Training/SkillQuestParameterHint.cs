#nullable enable
using ImGuiNET;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Styling;

namespace T3.Editor.Skills.Training;

/// <summary>
/// Decorations for the parameter window while a SkillQuest level is active: a status icon at
/// the revert slot of the name button, and a colored line prepended to the parameter tooltip.
/// Kept separate from <c>InputValueUi</c> so the per-parameter rendering code stays focused on
/// general editing concerns.
/// </summary>
internal static class SkillQuestParameterHint
{
    internal enum HintKind { Tip, Blocker }

    internal readonly record struct Hint(HintKind Kind, Icon Icon, Color Color, string Message);

    /// <summary>
    /// Resolves the SkillQuest hint for a parameter, if any. Returns false when no level is
    /// active or the parameter is not in a flagged state. Does *not* apply visibility rules
    /// (focus, fade-in) — use <see cref="TryGetVisible"/> for rendering decisions.
    /// </summary>
    internal static bool TryGet(Guid childId, Guid inputId, out Hint hint)
    {
        hint = default;
        var feedback = SkillTraining.ActiveLevelFeedback;
        if (feedback == null)
            return false;

        if (!feedback.TryGetParameterState(childId, inputId, out var paramState))
            return false;

        switch (paramState)
        {
            case LevelFeedback.ParamState.Required:
                hint = new Hint(HintKind.Tip, Icon.Tip, UiColors.StatusActivated,
                                "Try tweaking this parameter…");
                return true;
            case LevelFeedback.ParamState.Warm:
                hint = new Hint(HintKind.Tip, Icon.Tip, UiColors.StatusActivated,
                                "You're close — keep tweaking…");
                return true;
            case LevelFeedback.ParamState.Forbidden:
                hint = new Hint(HintKind.Blocker, Icon.Warning, UiColors.StatusWarning,
                                "This parameter isn't part of the solution. Revert it.");
                return true;
            default:
                return false; // Correct or anything else: no hint.
        }
    }

    /// <summary>
    /// Resolves the *visible* hint for a parameter, applying the SkillQuest visibility rules:
    /// all blockers are shown, but among Required/Warm only the focused "what's next" tip
    /// surfaces. Returns false when the icon should not be drawn (no hint, not focused, or the
    /// shared fade-in alpha has not started yet).
    /// </summary>
    internal static bool TryGetVisible(Guid childId, Guid inputId, out Hint hint, out float alpha)
    {
        alpha = 0;
        hint = default;
        var feedback = SkillTraining.ActiveLevelFeedback;
        if (feedback == null)
            return false;

        if (TryGet(childId, inputId, out hint))
        {
            if (hint.Kind == HintKind.Tip && !feedback.IsFocusedTip(childId, inputId))
                return false;
        }
        else if (ImGui.IsAnyItemActive() && feedback.IsFocusedTip(childId, inputId))
        {
            // The focused tip just hit Correct mid-drag (bit-exact match while sweeping
            // through the solution). Keep the icon + arc visible until the user releases
            // so the visualization doesn't pop out and back in.
            hint = new Hint(HintKind.Tip, Icon.Tip, UiColors.StatusActivated,
                            "You're close — keep tweaking…");
        }
        else
        {
            return false;
        }

        alpha = feedback.GetHintAlpha(childId, inputId);
        return alpha > 0f;
    }

    /// <summary>
    /// Draws the hint icon at the parameter-name button's revert-icon slot. For Tip hints,
    /// also draws a small progress arc behind the icon reflecting how close the current
    /// value is to the solution. Must be called while the name button is still ImGui's last
    /// item. Uses the window draw list so it does not change last-item state.
    /// </summary>
    internal static void DrawIcon(Guid childId, Guid inputId, in Hint hint, float alpha)
    {
        // Same offset as the hover-only Revert icon so the two line up. Callers skip this when
        // the Revert icon would take over (hovered AND non-default value).
        var pos = ImGui.GetItemRectMin() + new Vector2(6, 4) * T3Ui.UiScaleFactor;
        var dl = ImGui.GetWindowDrawList();

        if (hint.Kind == HintKind.Tip)
        {
            var proximity = SkillTraining.ActiveLevelFeedback?.GetTipProximity(childId, inputId) ?? 0f;
            DrawProximityArc(dl, pos, proximity, alpha);
        }

        Icons.DrawIconAtScreenPosition(hint.Icon, pos, dl, hint.Color.Fade(alpha));
    }

    /// <summary>
    /// Renders a faint ring around the Tip icon with a filled arc proportional to
    /// <paramref name="proximity"/>. A negative proximity means the parameter type has no
    /// meaningful progress (bool, enum, unsupported) — the ring is skipped entirely so the
    /// icon stays alone. Color shifts from the tip blue to
    /// <see cref="UiColors.StatusControlled"/> green as the value approaches the snapshot.
    /// </summary>
    private static void DrawProximityArc(ImDrawListPtr dl, Vector2 iconPos, float proximity, float alpha)
    {
        if (alpha <= 0f || proximity < 0f)
            return;

        var scale = T3Ui.UiScaleFactor;
        var center = iconPos + new Vector2(7.5f) * scale;
        var radius = 10 * scale;
        var thickness = 1.5f * scale;

        var bgColor = UiColors.BackgroundFull.Fade(0.2f * alpha);
        dl.AddCircle(center, radius, bgColor, 24, thickness);

        if (proximity <= 0f)
            return;

        // Soften the response curve before drawing — the raw log-scaled proximity feels 
        // hair-trigger near the solution; a gentle gain/bias dampens that without
        // changing the underlying scoring used elsewhere.
        var visual = proximity.ApplyGainAndBias(0.2f, 0.2f);

        var progressColor = Color.Mix(UiColors.ForegroundFull.Fade(0.2f), UiColors.ForegroundFull.Fade(0.8f), visual).Fade(alpha);
        var start = -MathF.PI * 0.5f;
        var end = start + MathF.Tau * visual;
        dl.PathArcTo(center, radius, start, end, 32);
        dl.PathStroke(progressColor, ImDrawFlags.None, thickness);
    }

    /// <summary>
    /// Emits the SkillQuest hint line at the top of an already-open tooltip, followed by a
    /// small vertical gap. Caller is responsible for opening/closing the tooltip.
    /// </summary>
    internal static void DrawTooltipPrefix(in Hint hint)
    {
        ImGui.TextColored(hint.Color, hint.Message);
        FormInputs.AddVerticalSpace(4);
    }
}
