using System.Collections.Frozen;
using System.Numerics;
using T3.SystemUi;
using MouseButtons = T3.SystemUi.MouseButtons;

namespace T3.MsForms;

internal sealed class CursorWrapper : ICursor
{
    public Point Position => _position;
    public MouseButtons ButtonState => _buttonState;
    public event EventHandler<MouseButtonEventArgs>? ButtonChanged;
    public event EventHandler<MouseState>? MouseChanged;

    public void SetVisible(bool visible)
    {
        if (visible)
            Cursor.Show();
        else
            Cursor.Hide();
    }

    private void HandleMouseUp(object? sender, MouseEventArgs e)
    {
        var newPos = e.Location;
        var hasNewPos = newPos != _position;
        var buttonsChanged = SetMouseButtons(e.Button, false, out var buttonsThatChanged);
        
        if (buttonsChanged || hasNewPos)
        {
            HandleMouseChange(newPos, buttonsThatChanged);
        }
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        var newPos = e.Location;
        var hasNewPos = newPos != _position;
        var buttonsChanged = SetMouseButtons(e.Button, true, out var buttonsThatChanged);

        if (buttonsChanged || hasNewPos)
        {
            HandleMouseChange(newPos, buttonsThatChanged);
        }
    }

    bool SetMouseButtons(System.Windows.Forms.MouseButtons buttonArgs, bool down, out MouseButtons buttonsThatChanged)
    {
        buttonsThatChanged = MouseButtons.None;
        var changed = false;
        
        foreach(var button in AllButtons)
        {
            if ((buttonArgs & button) == button)
            {
                var nativeButton = ButtonToNative[button];
                _buttonState = down ? _buttonState | nativeButton : _buttonState & ~nativeButton;
                buttonsThatChanged |= nativeButton;
                changed = true;
            }
        }

        return changed;
    }

    public void TrackMouseOf(Form form)
    {
        form.MouseMove += HandleMouseMove;
        form.MouseDown += HandleMouseDown;
        form.MouseUp += HandleMouseUp;
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e) => HandleMouseChange(e.Location, MouseButtons.None);

    private void HandleMouseChange(Point location, MouseButtons buttonsThatChanged)
    {
        _position = location;

        if (MouseChanged != null)
        {
            var state = new MouseState
                            {
                                ButtonState = _buttonState,
                                ButtonsThatChanged = buttonsThatChanged,
                                Position = new Vector2(_position.X, _position.Y)
                            };

            MouseChanged.Invoke(this, state);
        }
        
        if(buttonsThatChanged != MouseButtons.None && ButtonChanged != null)
        {
            ButtonChanged.Invoke(this, new MouseButtonEventArgs(buttonsThatChanged));
        }
    }

    private Point _position;

    private MouseButtons _buttonState;
    
    static CursorWrapper()
    {
        var allButtons = Enum.GetValues<System.Windows.Forms.MouseButtons>().ToList();
        allButtons.Remove(System.Windows.Forms.MouseButtons.None);
        AllButtons = allButtons.ToArray();
    }

    private static readonly System.Windows.Forms.MouseButtons[] AllButtons;
    private static readonly FrozenDictionary<System.Windows.Forms.MouseButtons, MouseButtons> ButtonToNative = new Dictionary<System.Windows.Forms.MouseButtons, MouseButtons>
            {
                { System.Windows.Forms.MouseButtons.Left, MouseButtons.Left },
                { System.Windows.Forms.MouseButtons.Right, MouseButtons.Right },
                { System.Windows.Forms.MouseButtons.Middle, MouseButtons.Middle },
                { System.Windows.Forms.MouseButtons.XButton1, MouseButtons.Back },
                { System.Windows.Forms.MouseButtons.XButton2, MouseButtons.Forward },
            }.ToFrozenDictionary();
}