using ImGuiNET;

namespace T3.Editor.Gui.UiHelpers;

/// <summary>
/// Framework for rendering modal dialogs.
///
/// Should be implemented like...
///
/// void SomeClass {
///     void Draw() {
///         _someDialog.Draw();
/// 
///        if(ImGui.Button()) {
///            _someDialog.ShowNextFrame();
///        }
///     }
/// }
/// 
/// void SomeDialog()
/// {
///     void Draw()
///     {
///         if(BeginDialog("myTitle"))
///         {    
/// 
///           // draw your content...
/// 
///           EndDialogContent();
///         }   
///         EndDialog();
///     }
/// }
/// 
/// </summary>
public abstract class ModalDialog
{
    internal void ShowNextFrame()
    {
        _shouldShowNextFrame = true;
        OnShowNextFrame();
    }
        
    // Todo - should be an abstract function other modal dialogs can use to initialize their values
    protected virtual void OnShowNextFrame(){}

    protected bool BeginDialog(string title)
    {
        if (_shouldShowNextFrame)
        {
            _shouldShowNextFrame = false;
            ImGui.OpenPopup(title);
        }


        // DialogSize is a minimum: the dialog grows with its content so primary actions at the
        // bottom are never cut off, and falls back to a scrollbar once it hits the screen limit.
        // Content must have intrinsic height — a bottom-anchored child (new Vector2(0, -h))
        // feeds back into AlwaysAutoResize and grows the window every frame.
        var minSize = DialogSize * T3Ui.UiScaleFactor;
        var maxHeight = MathF.Max(minSize.Y, ImGui.GetMainViewport().WorkSize.Y * 0.9f);
        ImGui.SetNextWindowSizeConstraints(minSize, new Vector2(minSize.X, maxHeight));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Padding, Padding));

        bool isOpen = true;
        // ImGuiWindowFlags.Popup is an internal-only flag in ImGui 1.90; passing it crashes the native call.
        if (!ImGui.BeginPopupModal(title, ref isOpen, Flags | ImGuiWindowFlags.AlwaysAutoResize))
            return false;
            
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, ItemSpacing);
        FrameStats.Current.OpenedPopUpName = title;
        FrameStats.Current.IsModalDialogOpen = true;
        FrameStats.Current.OpenedPopupHovered = ImGui.IsWindowHovered();
        return true;
    }

    /// <summary>
    /// Only call if BeginDialog returned true
    /// </summary>
    protected static void EndDialogContent()
    {
        ImGui.PopStyleVar();
        ImGui.EndPopup();
    }

    /// <summary>
    /// Call always
    /// </summary>
    protected static void EndDialog()
    {
        ImGui.PopStyleVar();
    }

    /// <summary>
    /// Padded modal for dialogs drawn from static code that can't subclass <see cref="ModalDialog"/>.
    /// The global style has zero window padding, so a plain <see cref="ImGui.BeginPopupModal(string)"/>
    /// renders its content flush against the frame. Pair with <see cref="EndStaticDialog"/> when it
    /// returns true; on false everything is already unwound.
    /// </summary>
    internal static bool BeginStaticDialog(string title, ref bool isOpen, ImGuiWindowFlags flags = ImGuiWindowFlags.AlwaysAutoResize)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(DefaultPadding, DefaultPadding) * T3Ui.UiScaleFactor);
        if (!ImGui.BeginPopupModal(title, ref isOpen, flags))
        {
            ImGui.PopStyleVar();
            return false;
        }

        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, DefaultItemSpacing * T3Ui.UiScaleFactor);
        return true;
    }

    internal static void EndStaticDialog()
    {
        ImGui.PopStyleVar(2);
        ImGui.EndPopup();
    }

    private bool _shouldShowNextFrame;
    protected Vector2 DialogSize = new(500, 350);
    protected Vector2 ItemSpacing = DefaultItemSpacing;
    protected float Padding = DefaultPadding;
    protected ImGuiWindowFlags Flags = ImGuiWindowFlags.None;

    private const float DefaultPadding = 20;
    private static readonly Vector2 DefaultItemSpacing = new(4, 10);
}