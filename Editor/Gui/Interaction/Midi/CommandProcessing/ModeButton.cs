namespace T3.Editor.Gui.Interaction.Midi.CommandProcessing;

public sealed class ModeButton
{
    /// <summary>
    /// While holding switches the device into an input mode 
    /// </summary>
    public ModeButton(ButtonRange buttonRange, CompatibleMidiDevice.InputModes mode, Interactions interaction= Interactions.ActiveWhileHolding)
    {
        ButtonRange = buttonRange;
        Mode = mode;
        Interaction = interaction;
    }

    public enum Interactions
    {
        ActiveWhileHolding,
        ToggleOnPress,
    }

    public Interactions Interaction;
    public ButtonRange ButtonRange;
    public CompatibleMidiDevice.InputModes Mode;
}