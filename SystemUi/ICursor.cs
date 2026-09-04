using System.Collections.Frozen;
using System.Drawing;
using System.Numerics;

namespace T3.SystemUi;

public interface ICursor
{
    public Vector2 PositionVec
    {
        get
        {
            var position = Position;
            return new Vector2(position.X, position.Y);
        }
    }

    public Point Position { get; }
    
    public MouseButtons ButtonState { get; }
    
    /// <summary>
    /// Returns if button is down based on the conventional button "index"
    /// </summary>
    /// <param name="buttonIndex">(0 = left, 1 = right, 2 = middle, 3 = back, 4 = forward)</param>
    /// <returns>True is button is currently pressed</returns>
    public bool IsButtonDown(int buttonIndex)
    {
        return IsButtonDown(MouseButtonConversion.IndexToButton[buttonIndex]);
    }
    
    public bool IsButtonDown(MouseButtons button) => (ButtonState & button) == button;
    public void SetVisible(bool visible);
    public event EventHandler<MouseButtonEventArgs> ButtonChanged;
    public event EventHandler<MouseState> MouseChanged;
}

public record struct MouseState(MouseButtons ButtonState, MouseButtons ButtonsThatChanged, Vector2 Position);

public static class MouseButtonConversion // for array-users
{
    public static void ButtonsToArray(MouseButtons buttons, Span<bool> array)
    {
        var length = ButtonCount;
        if(array.Length != length)
            throw new ArgumentException("Provided button span must have a length of 5", nameof(array));

        for (int i = 0; i < length; i++)
        {
            var button = IndexToButton[i];
            array[i] = (buttons & button) == button;
        }
    }
    
    // ReSharper disable once UnusedMember.Global
    public static void ToBooleanArray(MouseButtons buttons, Span<bool> array) => ButtonsToArray(buttons, array);
    
    public static MouseButtons ArrayToButtons(ReadOnlySpan<bool> array)
    {
        var length = ButtonCount;
        if(array.Length != length)
            throw new ArgumentException("Provided button span must have a length of 5", nameof(array));
        
        var buttons = MouseButtons.None;
        for(int i = 0; i < length; i++)
        {
            if(array[i])
                buttons |= IndexToButton[i];
        }

        return buttons;
    }
    
    public static bool IsButtonPressed(this MouseButtons buttons, MouseButtons button) => (buttons & button) == button;

    public static readonly FrozenDictionary<MouseButtons, int> ButtonToIndex = new Dictionary<MouseButtons, int>
                                                                                   {
                                                                                       {MouseButtons.Left, 0},
                                                                                       {MouseButtons.Right, 1},
                                                                                       {MouseButtons.Middle, 2},
                                                                                       {MouseButtons.Back, 3},
                                                                                       {MouseButtons.Forward, 4}
                                                                                   }.ToFrozenDictionary();

    static MouseButtonConversion()
    {
        IndexToButton = ButtonToIndex.ToDictionary(kvp => kvp.Value, kvp => kvp.Key).ToFrozenDictionary();
    }

    public static readonly FrozenDictionary<int, MouseButtons> IndexToButton;
    private static readonly MouseButtons[] AllButtons = Enum.GetValues<MouseButtons>();
    private static readonly int ButtonCount = AllButtons.Length;
}

public sealed class MouseButtonEventArgs(MouseButtons button) : EventArgs
{
    public bool Consumed { get; private set; }

    public readonly MouseButtons Button = button;

    public void Consume()
    {
        Consumed = true;
    }
}

[Flags]
public enum MouseButtons : byte
{
    None = 0,
    Left = 1,
    Right = 2,
    Middle = 4,
    Back = 8,
    Forward = 16
}
