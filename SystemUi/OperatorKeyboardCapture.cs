namespace T3.SystemUi;

/// <summary>
/// Lets an operator that is used directly in an output window (e.g. Sketch) take over the editor's keyboard
/// shortcuts. Claims are collected during a frame and take effect in the next one, so the result doesn't depend
/// on the order in which windows are drawn and operators are evaluated.
/// </summary>
public static class OperatorKeyboardCapture
{
    /// <summary>
    /// Operators call this every frame they can use the keyboard, typically while they are selected.
    /// The editor only grants it while an output window is hovered or focused and no text input is active.
    /// </summary>
    public static void Claim(Guid symbolChildId)
    {
        _claimedThisFrame = symbolChildId;
    }

    /// <summary>True while the keyboard belongs to this operator. Operators should ignore keys otherwise.</summary>
    public static bool IsCapturedBy(Guid symbolChildId) => symbolChildId != Guid.Empty && CapturedBy == symbolChildId;

    /// <summary>The operator that currently holds the keyboard, or <see cref="Guid.Empty"/>.</summary>
    public static Guid CapturedBy { get; private set; }

    /// <summary>Called by output windows every frame they are hovered or focused.</summary>
    public static void MarkOutputWindowActive()
    {
        _outputWindowActiveThisFrame = true;
    }

    /// <summary>Called by the editor once at the start of each frame.</summary>
    public static void BeginFrame(bool isTextInputActive)
    {
        CapturedBy = _outputWindowActiveThisFrame && !isTextInputActive ? _claimedThisFrame : Guid.Empty;
        _claimedThisFrame = Guid.Empty;
        _outputWindowActiveThisFrame = false;
    }

    private static Guid _claimedThisFrame;
    private static bool _outputWindowActiveThisFrame;
}
