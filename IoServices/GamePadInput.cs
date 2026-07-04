namespace T3.Core.IO;

public struct DirectionalPadState
{
    public bool Left, Right, Up, Down;

    public DirectionalPadState(bool left, bool right, bool up, bool down)
    {
        this.Left = left;
        this.Right = right;
        this.Up = up;
        this.Down = down;
    }
}

public struct ButtonsState
{
    public bool A, B, X, Y;

    public ButtonsState(bool a, bool b, bool x, bool y)
    {
        this.A = a;
        this.B = b; 
        this.X = x;
        this.Y = y;
    }
}

public struct GamePadState
{
    public DirectionalPadState DirectionalPad;
    public ButtonsState Buttons;

    public bool Back;
    public bool Start;

    public Vector2 LeftThumb;
    public Vector2 RightThumb;

    public bool LeftStickButton;
    public bool RightStickButton;

    public float LeftTrigger;
    public float RightTrigger;

    public bool LeftShoulder;
    public bool RightShoulder;

}