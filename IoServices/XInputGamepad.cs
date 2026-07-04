using SharpDX.XInput;


namespace T3.Core.IO;

public sealed class XInputGamepad
{
    private const float LeftDeadZone = (float)SharpDX.XInput.Gamepad.LeftThumbDeadZone / (float)short.MaxValue;
    private const float RightDeadZone = (float)SharpDX.XInput.Gamepad.RightThumbDeadZone / (float)short.MaxValue;

    public static GamePadState GetState(SharpDX.XInput.Controller controller)
    {
        if (controller == null)
            return new GamePadState();
        else
            return Create(controller.GetState().Gamepad);
    }

    public static GamePadState Create(Gamepad gamepad)
    {
        GamePadState result = new GamePadState();

        result.DirectionalPad = new DirectionalPadState
            (
             gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadLeft),
             gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadRight),
             gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadUp),
             gamepad.Buttons.HasFlag(GamepadButtonFlags.DPadDown)
            );

        result.Buttons  = new ButtonsState
            (
             gamepad.Buttons.HasFlag(GamepadButtonFlags.A),
             gamepad.Buttons.HasFlag(GamepadButtonFlags.B),
             gamepad.Buttons.HasFlag(GamepadButtonFlags.X),
             gamepad.Buttons.HasFlag(GamepadButtonFlags.Y)
                
            );

        result.Back = gamepad.Buttons.HasFlag(GamepadButtonFlags.Back);
        result.Start = gamepad.Buttons.HasFlag(GamepadButtonFlags.Start);

        result.LeftShoulder = gamepad.Buttons.HasFlag(GamepadButtonFlags.LeftShoulder);
        result.RightShoulder = gamepad.Buttons.HasFlag(GamepadButtonFlags.RightShoulder);

        result.LeftTrigger = (float)gamepad.LeftTrigger / 255.0f;
        result.RightTrigger = (float)gamepad.RightTrigger / 255.0f;

        result.LeftThumb = new Vector2(ApplyDeadZone(gamepad.LeftThumbX / (float)short.MaxValue, LeftDeadZone), ApplyDeadZone(gamepad.LeftThumbY / (float)short.MaxValue, LeftDeadZone));
        result.RightThumb = new Vector2(ApplyDeadZone(gamepad.RightThumbX / (float)short.MaxValue, RightDeadZone), ApplyDeadZone(gamepad.RightThumbY / (float)short.MaxValue, RightDeadZone));

        result.LeftStickButton = gamepad.Buttons.HasFlag(GamepadButtonFlags.LeftThumb);
        result.RightStickButton = gamepad.Buttons.HasFlag(GamepadButtonFlags.RightThumb);

        return result;
    }

    private static float ApplyDeadZone(float value, float deadZone)
    {

        if (value > 0.0f)
        {
            value -= deadZone;
            if (value < 0.0f)
            {
                value = 0.0f;
            }
        }
        else
        {
            value += deadZone;
            if (value > 0.0f)
            {
                value = 0.0f;
            }
        }
        // Renormalize the value according to the dead zone
        value = value / (1.0f - deadZone);
        return value < -1.0f ? -1.0f : value > 1.0f ? 1.0f : value;
    }
}