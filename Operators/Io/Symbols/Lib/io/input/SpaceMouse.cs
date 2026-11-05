using T3.Core.IO;
using T3.IoServices;

namespace Lib.io.input;

[Guid("284beba2-abf3-43d0-b8fb-8c3fc348a0be")]
internal sealed class SpaceMouse : Instance<SpaceMouse>, IStatusProvider
{
    [Output(Guid = "e4f6c192-0b2f-43af-a1f1-1a5d38bcbcfd", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Dict<float>> State = new();

    [Output(Guid = "7c617e24-a001-4e9e-90da-a3a40ee27e10", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Translation = new();

    [Output(Guid = "b0c41336-4e7e-472e-8946-d9fe20122708", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Vector3> Rotation = new();

    [Output(Guid = "bea02787-cd81-421e-bb62-f5ccb1ec0ac4", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> IsConnected = new();

    public SpaceMouse()
    {
        State.UpdateAction = Update;
        Translation.UpdateAction = Update;
        Rotation.UpdateAction = Update;
        IsConnected.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var isConnected = SpaceMouseInput.State == SpaceMouseInput.States.Connected;
        IsConnected.Value = isConnected;
        _lastErrorMessage = !isConnected
                                ? "No SpaceMouse connected (see Settings → Input Devices)"
                                : !SpaceMouseInput.IsAvailableToOperators
                                    ? "The SpaceMouse flies the camera. Switch it to operators in Settings → Input Devices."
                                    : string.Empty;

        var translation = Vector3.Zero;
        var rotation = Vector3.Zero;
        var button0 = false;
        var button1 = false;
        if (isConnected && SpaceMouseInput.IsAvailableToOperators)
        {
            translation = ToTixlAxes(SpaceMouseInput.Translation);
            rotation = ToTixlAxes(SpaceMouseInput.Rotation);
            button0 = SpaceMouseInput.IsButton0Down;
            button1 = SpaceMouseInput.IsButton1Down;
        }

        Translation.Value = translation;
        Rotation.Value = rotation;

        _floatDict["TranslationX"] = translation.X;
        _floatDict["TranslationY"] = translation.Y;
        _floatDict["TranslationZ"] = translation.Z;
        _floatDict["RotationX"] = rotation.X;
        _floatDict["RotationY"] = rotation.Y;
        _floatDict["RotationZ"] = rotation.Z;
        _floatDict["Button0"] = button0 ? 1 : 0;
        _floatDict["Button1"] = button1 ? 1 : 0;
        State.Value = _floatDict;

        State.DirtyFlag.Clear();
        Translation.DirtyFlag.Clear();
        Rotation.DirtyFlag.Clear();
        IsConnected.DirtyFlag.Clear();
    }

    /// <summary>
    /// The device reports X right, Y towards the user and Z down (HID). TiXL has Y up and looks down -Z, so pushing the
    /// cap away moves -Z and lifting it moves +Y. Normalized to about -1..1 at full deflection.
    /// </summary>
    private static Vector3 ToTixlAxes(Vector3 hid)
    {
        var scaled = hid / SpaceMouseInput.FullDeflection;
        return Vector3.Clamp(new Vector3(scaled.X, -scaled.Z, scaled.Y), -Vector3.One, Vector3.One);
    }

    private readonly Dict<float> _floatDict = new(0);
    private string _lastErrorMessage = string.Empty;

    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;
}
