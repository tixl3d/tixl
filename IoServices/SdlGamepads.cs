#nullable enable
using System.Numerics;
using SDL;
using T3.Core.Logging;
using static SDL.SDL3;

namespace T3.IoServices;

/// <summary>
/// Reads game controllers through SDL for operators and the editor. Pads are numbered in connection order, and SDL
/// maps Xbox, PlayStation and Switch pads to the same layout (A is the bottom face button).
/// </summary>
/// <remarks>
/// The host calls <see cref="Initialize"/> after <c>SDL_Init</c> and <see cref="Update"/> once per frame on the main
/// thread after pumping SDL events, which is what refreshes the pad states. Everyone reading in that frame gets the
/// same snapshot.
/// </remarks>
public static unsafe class SdlGamepads
{
    public static int Count => _pads.Count;

    /** False if SDL's gamepad subsystem failed to start; then no controller can be read at all. */
    public static bool IsAvailable => _isInitialized;

    /** When input last arrived from any pad, for activity indicators; 0 if never. */
    public static long LastActivityTicksMs { get; private set; }

    /// <summary>
    /// False while the editor uses the pads for its own control. Operators then see a released pad, so steering the
    /// editor doesn't also steer the scene.
    /// </summary>
    public static bool IsAvailableToOperators { get; set; } = true;

    public static void Initialize()
    {
        // XInput kept working while another application had the focus, and a performance relies on that
        SDL_SetHint(SDL_HINT_JOYSTICK_ALLOW_BACKGROUND_EVENTS, "1");

        if (!SDL_InitSubSystem(SDL_InitFlags.SDL_INIT_GAMEPAD))
        {
            Log.Warning($"Game controllers are not available: {SDL_GetError()}");
            return;
        }

        _isInitialized = true;
        ScanForPads();
    }

    public static void Shutdown()
    {
        if (!_isInitialized)
            return;

        for (var index = 0; index < _pads.Count; index++)
        {
            SDL_CloseGamepad(_pads[index].Handle);
        }

        _pads.Clear();
        SDL_QuitSubSystem(SDL_InitFlags.SDL_INIT_GAMEPAD);
        _isInitialized = false;
    }

    public static void Update()
    {
        if (!_isInitialized)
            return;

        var now = Environment.TickCount64;
        if (now - _lastScanTicksMs > ScanIntervalMs)
            ScanForPads();

        for (var index = _pads.Count - 1; index >= 0; index--)
        {
            var pad = _pads[index];
            if (!SDL_GamepadConnected(pad.Handle))
            {
                Log.Debug($"Game controller disconnected: {pad.Name}");
                SDL_CloseGamepad(pad.Handle);
                _pads.RemoveAt(index);
                continue;
            }

            pad.State = ReadState(pad.Handle);
            if (!IsReleased(pad.State))
                LastActivityTicksMs = now;
        }
    }

    /** Looks for newly connected controllers right away instead of waiting for the next periodic scan. */
    public static void Rescan()
    {
        if (_isInitialized)
            ScanForPads();
    }

    /// <returns>False if no pad is connected at <paramref name="index"/></returns>
    public static bool TryGetState(int index, out GamePadState state)
    {
        if (index < 0 || index >= _pads.Count)
        {
            state = default;
            return false;
        }

        state = _pads[index].State;
        return true;
    }

    /// <summary>Like <see cref="TryGetState"/>, but reports a released pad while the editor uses it.</summary>
    public static bool TryGetStateForOperators(int index, out GamePadState state)
    {
        if (!TryGetState(index, out state))
            return false;

        if (!IsAvailableToOperators)
            state = default;

        return true;
    }

    public static string GetName(int index)
    {
        return index >= 0 && index < _pads.Count ? _pads[index].Name : string.Empty;
    }

    /** Opens pads that were plugged in. Removed pads are dropped every frame through SDL_GamepadConnected. */
    private static void ScanForPads()
    {
        _lastScanTicksMs = Environment.TickCount64;

        int count;
        var ids = SDL_GetGamepads(&count);
        if (ids == null)
            return;

        for (var idIndex = 0; idIndex < count; idIndex++)
        {
            var id = ids[idIndex];
            if (IsOpen(id))
                continue;

            var handle = SDL_OpenGamepad(id);
            if (handle == null)
            {
                Log.Warning($"Can't open game controller: {SDL_GetError()}");
                continue;
            }

            var name = SDL_GetGamepadName(handle) ?? "Game controller";
            _pads.Add(new Pad(id, handle, name));
            Log.Debug($"Game controller connected: {name}");
        }

        SDL_free(ids);
    }

    private static bool IsOpen(SDL_JoystickID id)
    {
        for (var index = 0; index < _pads.Count; index++)
        {
            if (_pads[index].Id == id)
                return true;
        }

        return false;
    }

    private static GamePadState ReadState(SDL_Gamepad* handle)
    {
        var state = new GamePadState
                        {
                            DirectionalPad = new DirectionalPadState(IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT),
                                                                     IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT),
                                                                     IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP),
                                                                     IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN)),
                            Buttons = new ButtonsState(IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH),
                                                       IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST),
                                                       IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST),
                                                       IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH)),
                            Back = IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK),
                            Start = IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START),
                            LeftShoulder = IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER),
                            RightShoulder = IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER),
                            LeftStickButton = IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK),
                            RightStickButton = IsDown(handle, SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK),
                            LeftThumb = ReadStick(handle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY,
                                                  LeftStickDeadZone),
                            RightThumb = ReadStick(handle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY,
                                                   RightStickDeadZone),
                            LeftTrigger = ReadAxis(handle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER),
                            RightTrigger = ReadAxis(handle, SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER),
                        };
        return state;
    }

    private static bool IsDown(SDL_Gamepad* handle, SDL_GamepadButton button)
    {
        return SDL_GetGamepadButton(handle, button);
    }

    private static float ReadAxis(SDL_Gamepad* handle, SDL_GamepadAxis axis)
    {
        return Math.Clamp(SDL_GetGamepadAxis(handle, axis) / (float)short.MaxValue, -1, 1);
    }

    /// <summary>
    /// Applies a radial dead zone and rescales the rest to the full range, so a light touch starts at zero instead of
    /// jumping to the dead zone's edge. Up is positive, as with XInput; SDL reports down as positive.
    /// </summary>
    private static Vector2 ReadStick(SDL_Gamepad* handle, SDL_GamepadAxis axisX, SDL_GamepadAxis axisY, float deadZone)
    {
        var stick = new Vector2(ReadAxis(handle, axisX), -ReadAxis(handle, axisY));
        var length = stick.Length();
        if (length <= deadZone)
            return Vector2.Zero;

        var scaledLength = MathF.Min(1, (length - deadZone) / (1 - deadZone));
        return stick * (scaledLength / length);
    }

    private static bool IsReleased(in GamePadState state)
    {
        return state.LeftThumb == Vector2.Zero
               && state.RightThumb == Vector2.Zero
               && state.LeftTrigger < TriggerActivityThreshold
               && state.RightTrigger < TriggerActivityThreshold
               && state.DirectionalPad is { Left: false, Right: false, Up: false, Down: false }
               && state.Buttons is { A: false, B: false, X: false, Y: false }
               && !state.Back && !state.Start
               && !state.LeftShoulder && !state.RightShoulder
               && !state.LeftStickButton && !state.RightStickButton;
    }

    private sealed class Pad(SDL_JoystickID id, SDL_Gamepad* handle, string name)
    {
        internal readonly SDL_JoystickID Id = id;
        internal readonly SDL_Gamepad* Handle = handle;
        internal readonly string Name = name;
        internal GamePadState State;
    }

    /** XInput's recommended dead zones, which the operators were tuned with. */
    private const float LeftStickDeadZone = 7849f / short.MaxValue;

    private const float RightStickDeadZone = 8689f / short.MaxValue;

    /** Triggers rest slightly above zero on some pads. */
    private const float TriggerActivityThreshold = 0.05f;

    private const long ScanIntervalMs = 500;

    private static readonly List<Pad> _pads = [];
    private static bool _isInitialized;
    private static long _lastScanTicksMs;
}
