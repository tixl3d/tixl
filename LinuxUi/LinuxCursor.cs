using System.Drawing;
using System.Numerics;
using T3.SystemUi;

namespace T3.LinuxUi;

/// <summary>
/// Linux cursor implementation. In the future this will use Silk.NET input,
/// but for now provides a stub that the ImGui layer can drive.
/// </summary>
public sealed class LinuxCursor : ICursor
{
    public Point Position { get; set; }
    public MouseButtons ButtonState { get; set; }

    public void SetVisible(bool visible)
    {
        // TODO: implement via Silk.NET cursor visibility
    }

    public event EventHandler<MouseButtonEventArgs>? ButtonChanged;
    public event EventHandler<MouseState>? MouseChanged;

    internal void UpdatePosition(int x, int y)
    {
        Position = new Point(x, y);
        MouseChanged?.Invoke(this, new MouseState(ButtonState, MouseButtons.None, new Vector2(x, y)));
    }

    internal void UpdateButton(MouseButtons button, bool pressed)
    {
        if (pressed)
            ButtonState |= button;
        else
            ButtonState &= ~button;

        ButtonChanged?.Invoke(this, new MouseButtonEventArgs(button));
    }
}
