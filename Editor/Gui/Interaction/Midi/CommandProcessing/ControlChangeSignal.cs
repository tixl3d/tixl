namespace T3.Editor.Gui.Interaction.Midi.CommandProcessing;

/// <summary>
/// Used to save captured note and control events from MidiInputs
/// </summary>
public sealed class ControlChangeSignal
{
    public int ControllerId;
    public float ControllerValue;
    public int Channel;
}