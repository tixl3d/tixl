namespace T3.Editor.Gui.UiHelpers;

public class UIControlledSetting
{
    /// <summary>
    /// The generated unique label
    /// </summary>
    private string _uniqueLabel;
    private string _hiddenUniqueLabel;

    /// <summary>
    /// The label provided in the constructor
    /// </summary>
    public string CleanLabel { get; private set; }
    public bool DrawOnLeft { get; private set; }

    public string Tooltip { get; private set; }
    private string _additionalNotes;
    private Func<string, bool> _guiFunc;
    private Action _OnValueChanged;

    // Cheaper than GUIDs
    // In case we want to have the same variable be changeable with different UI controls
    // or if multiple settings have the same label
    private static ushort _countForUniqueID = ushort.MaxValue;

    /// <summary>
    /// For the sake of simple use of the optional parameters and populating/maintaining many settings, the recommended way to call this constructor is:
    /// <code>
    /// new UIControlledSetting
    /// (
    ///    label: "My Setting",
    ///    tooltip: "The global scale of all rendered UI in the application",
    ///    guiFunc: (string guiLabel) => CustomComponents.FloatValueEdit(guiLabel, ref UserSettings.Config.UiScaleFactor, 0.01f, 0.5f, 3f),
    ///    OnValueChanged: () => //your action
    /// );
    /// </code>
    /// </summary>
    /// <param name="label">The label to display next to the gui control</param>
    /// <param name="guiFunc">The <see cref="ImGuiNET"/> - based function that draws the setting control and
    /// returns true if the control was changed. The input to this function see is a unique ID based on the label provided</param>
    /// <param name="tooltip">Tooltip displayed when hovering over the control</param>
    /// <param name="additionalNotes">Additional notes displayed alongside the tooltip [Not currently in use, but will be once T3 tooltips are integrated]</param>
    /// <param name="OnValueChanged">An action performed when the value is changed</param>
    public UIControlledSetting(string label, Func<string, bool> guiFunc, string tooltip = null, string additionalNotes = null, bool drawOnLeft = false, Action OnValueChanged = null)
    {
        CleanLabel = label;
        _hiddenUniqueLabel = $"##{label}{_countForUniqueID--}";
        _uniqueLabel = $"{label}##{_countForUniqueID--}";

        _guiFunc = guiFunc;
        Tooltip = tooltip;
        _additionalNotes = additionalNotes;
        _OnValueChanged = OnValueChanged;
        DrawOnLeft = drawOnLeft;
    }

    /// <summary>
    /// Draws the GUI for this setting using the Func provided in its constructor
    /// </summary>
    /// <returns>True if changed, false if unchanged.
    /// If an Action was provided in constructor, it will be executed when value is changed. </returns>
    public bool DrawGUIControl(bool hideLabel)
    {
        var changed = DrawCommand(hideLabel);

        if (changed)
        {
            _OnValueChanged?.Invoke();
        }

        return changed;
    }

    private bool DrawCommand(bool hideLabel)
    {
        return _guiFunc.Invoke(hideLabel ? _hiddenUniqueLabel : _uniqueLabel);
    }
}