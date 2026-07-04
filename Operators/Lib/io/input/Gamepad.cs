using T3.Core.IO;
using T3.IoServices;
using T3.Core.Utils;

namespace Lib.io.input;

[Guid("d69e0f2e-8fe2-478b-ba4e-2a55a92670ae")]
internal sealed class Gamepad : Instance<Gamepad>, IStatusProvider
{
    [Output(Guid = "64E9DEC6-2B3A-4D7E-A174-991C8A8B929E", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<Dict<float>> State = new();

    [Output(Guid = "49907A55-54FA-4C95-8E00-E7BEED734533", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> IsConnected = new();
        
    public Gamepad()
    {
        State.UpdateAction = Update;
        IsConnected.UpdateAction = Update;
    }

    private void Update(EvaluationContext context)
    {
        var index = Index.GetValue(context).Clamp(0,3);
        var controllers = new SharpDX.XInput.Controller[4];
        controllers[0] = new SharpDX.XInput.Controller(SharpDX.XInput.UserIndex.One);
        controllers[1] = new SharpDX.XInput.Controller(SharpDX.XInput.UserIndex.Two);
        controllers[2] = new SharpDX.XInput.Controller(SharpDX.XInput.UserIndex.Three);
        controllers[3] = new SharpDX.XInput.Controller(SharpDX.XInput.UserIndex.Four);

        var currentController = controllers[index];
        if (!currentController.IsConnected)
        {
            _lastErrorMessage = $"Controller {index} is not connected";
            IsConnected.Value = false;
            return;
        }
            
        _lastErrorMessage = "";
        IsConnected.Value = true;
            
        var state = XInputGamepad.GetState(currentController);

        _floatDict["LeftThumbX"] = state.LeftThumb.X;
        _floatDict["LeftThumbY"] = state.LeftThumb.Y;
        _floatDict["RightThumbX"] = state.RightThumb.X;
        _floatDict["RightThumbY"] = state.RightThumb.Y;
            
        _floatDict["LeftTrigger"] = state.LeftTrigger;
        _floatDict["RightTrigger"] = state.RightTrigger;
            
        _floatDict["Directional/Pad.Left"] = state.DirectionalPad.Left?1:0;
        _floatDict["Directional/Pad.Right"] = state.DirectionalPad.Right?1:0;
        _floatDict["Directional/Pad.Up"] = state.DirectionalPad.Up?1:0;            
        _floatDict["Directional/Pad.Down"] = state.DirectionalPad.Down?1:0;             
            
        _floatDict["Buttons/A"] = state.Buttons.A?1:0;
        _floatDict["Buttons/B"] = state.Buttons.B?1:0;
        _floatDict["Buttons/X"] = state.Buttons.X?1:0;
        _floatDict["Buttons/Y"] = state.Buttons.Y?1:0;
            
        _floatDict["LeftTrigger"] = state.LeftTrigger;
        _floatDict["RightTrigger"] = state.RightTrigger;
        _floatDict["LeftShoulder"] = state.LeftShoulder?1:0;
        _floatDict["RightShoulder"] = state.RightShoulder ? 1:0;

        _floatDict["LeftStickButton"] = state.LeftStickButton ? 1 : 0;
        _floatDict["RightStickButton"] = state.RightStickButton ? 1 : 0;    

        _floatDict["Start"] = state.Start?1:0;
        _floatDict["Back"] = state.Back?1:0;
            
        State.Value = _floatDict;
    }

    private readonly Dict<float> _floatDict = new(0);
    private string _lastErrorMessage = "";
        
        
    IStatusProvider.StatusLevel IStatusProvider.GetStatusLevel() =>
        string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;

    string IStatusProvider.GetStatusMessage() => _lastErrorMessage;
        
    [Input(Guid = "b37ab751-1e41-4e58-9e27-5ac9052662d8")]
    public readonly InputSlot<int> Index = new();
}