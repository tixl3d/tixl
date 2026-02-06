using NAudio.Midi;
using Operators.Utils;
using T3.Core.Animation;
using T3.Core.Stats;
using T3.Core.Utils;

namespace Lib.io.midi;

[Guid("59a0458e-2f3a-4856-96cd-32936f783cc5")]
public sealed class MidiInput : Instance<MidiInput>, MidiConnectionManager.IMidiConsumer, IStatusProvider
{
    [Output(Guid = "01706780-D25B-4C30-A741-8B7B81E04D82")]
    public readonly Slot<float> Result = new();

    [Output(Guid = "D7114289-4B1D-47E9-B5C1-DCDC8A371087")]
    public readonly Slot<List<float>> Range = new();
        
    [Output(Guid = "4BF74648-207F-4275-83BA-09E1C048C33B")]
    public readonly Slot<bool> WasHit = new();
        
    public MidiInput()
    {
        Result.UpdateAction += Update;
        Range.UpdateAction += Update;
        WasHit.UpdateAction += Update;
    }

    private bool _initialized;
    private double _lastUpdateTime = -1;
        
    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        MidiConnectionManager.UnregisterConsumer(this);
    }

    private void Update(EvaluationContext context)
    {
        if (Math.Abs(context.LocalFxTime - _lastUpdateTime) < 0.0001f)
            return;
        
        _lastUpdateTime= context.LocalFxTime;
        
        if (!_initialized)
        {
            MidiConnectionManager.RegisterConsumer(this);
            _initialized = true;
        }
            
        _trainedDeviceName = Device.GetValue(context);

        if (MidiConnectionManager.TryGetMidiIn(_trainedDeviceName, out _))
        {
            _warningMessage = null;
            this.ClearErrorState();
        }
        else
        {
            _warningMessage = $"Midi device '{_trainedDeviceName}' is not captured.\nYou can try Windows » Settings » Midi » Rescan Devices.";
            this.LogWarningState(_warningMessage);
        }

        _trainedChannel = Channel.GetValue(context);
        _trainedControllerId = Control.GetValue(context);
        _trainedEventType = EventType.GetEnumValue<MidiEventTypes>(context);

        var defaultOutputValue = DefaultOutputValue.GetValue(context);
        var damping = Damping.GetValue(context);
        var outRange = OutputRange.GetValue(context);

        _controlRange = ControlRange.GetValue(context);

        _printLogMessages = PrintLogMessages.GetValue(context);

        var controlRangeSize = (_controlRange.Height - _controlRange.Width).Clamp(1, 128);

        if (_valuesForControlRange == null || _valuesForControlRange.Count != controlRangeSize)
        {
            _valuesForControlRange = new List<float>(controlRangeSize);
            _valuesForControlRange.AddRange(new float[controlRangeSize]); //initialize list values
        }

        if (MathUtils.WasTriggered(TeachTrigger.GetValue(context), ref _oldTeachTrigger))
        {
            //MidiInConnectionManager.Rescan();
            _teachingActive = true;
            _lastMatchingSignals.Clear();
            _currentControllerValue = 0;
        }

        var wasHit = false;
        lock (this)
        {
            foreach (var signal in _lastMatchingSignals)
            {
                if (_teachingActive)
                {
                    // The teaching mode shouldn't override the connected nodes
                    if (!Device.HasInputConnections) {
                        Device.SetTypedInputValue(_lastMessageDevice.ProductName);
                        _trainedDeviceName = _lastMessageDevice.ProductName;
                    }

                    if (!Channel.HasInputConnections) {
                        Channel.SetTypedInputValue(signal.Channel);
                        _trainedChannel = signal.Channel;
                    }

                    if (!Control.HasInputConnections) {
                        Control.SetTypedInputValue(signal.ControllerId);
                        _trainedControllerId = signal.ControllerId;
                    }

                    if (!EventType.HasInputConnections) {
                        EventType.SetTypedInputValue((int)signal.EventType);
                        _trainedEventType = signal.EventType;
                    }

                    TeachTrigger.SetTypedInputValue(false);
                    _teachingActive = false;
                }

                var hasValueChanged = Math.Abs(_currentControllerValue - signal.ControllerValue) > 0.001f;
                _currentControllerValue = signal.ControllerValue;
                _currentControllerId = signal.ControllerId;

                var isWithinControlRange =
                    signal.ControllerId >= _controlRange.Width &&
                    signal.ControllerId < _controlRange.Height;

                if (isWithinControlRange)
                {
                    var index = signal.ControllerId - _controlRange.Width;
                    _valuesForControlRange[index] = signal.ControllerValue;
                }

                if (hasValueChanged && signal.ControllerValue > 0)
                    wasHit = true;
                    
                LastMessageTime = Playback.RunTimeInSecs;
                _isDefaultValue = false;
            }

            _lastMatchingSignals.Clear();
        }
            
        if (ResetToDefaultTrigger.GetValue(context))
        {
            ResetToDefaultTrigger.SetTypedInputValue(false);
            _isDefaultValue = true;
        }

        
        if (_isDefaultValue && _trainedEventType != MidiEventTypes.MidiTime)
        {
            Result.Value = defaultOutputValue;
            Result.DirtyFlag.Clear();
            Range.DirtyFlag.Clear();
            WasHit.DirtyFlag.Clear();
            return;
        }
            
        var currentValue = UseControlRange
                               ? _currentControllerId
                               : MathUtils.Remap(_currentControllerValue, 0, 127, outRange.X, outRange.Y);

        if (_trainedEventType == MidiEventTypes.MidiTime)
        {
            currentValue = (float)(_timingMsgCount / (24.0 * 4 ));
        }
            

        _dampedOutputValue = MathUtils.Lerp(currentValue, _dampedOutputValue, damping);
        
        var reachTarget = MathF.Abs(_dampedOutputValue - currentValue) < 0.0001f;
        var needsUpdateNextFrame = !reachTarget || wasHit;
        Result.DirtyFlag.Trigger = needsUpdateNextFrame ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        Range.DirtyFlag.Trigger =  needsUpdateNextFrame ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;
        WasHit.DirtyFlag.Trigger = needsUpdateNextFrame ? DirtyFlagTrigger.Animated : DirtyFlagTrigger.None;

        if (reachTarget)
        {
            _dampedOutputValue = currentValue;
        }

        if (!float.IsNormal(_dampedOutputValue))
            _dampedOutputValue = 0;

        WasHit.Value = wasHit;
        Result.Value = _dampedOutputValue;
        Range.Value = _valuesForControlRange;
            
        // Result.DirtyFlag.Clear();
        // Range.DirtyFlag.Clear();
        // WasHit.DirtyFlag.Clear();
    }

    public void ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
    }
        

    /// <summary>
    /// This will cause update to be called on next frame 
    /// </summary>
    private void FlagAsDirty()
    {
        // Disable until invalidation is fixed
        // Result.DirtyFlag.Invalidate();
        // Range.DirtyFlag.Invalidate();
        // WasHit.DirtyFlag.Invalidate();
            
        Result.DirtyFlag.Trigger =  DirtyFlagTrigger.Animated;
        Range.DirtyFlag.Trigger =   DirtyFlagTrigger.Animated;
        WasHit.DirtyFlag.Trigger =  DirtyFlagTrigger.Animated;
    }

    /// <remarks>
    /// This comes in multi threaded
    /// </remarks>
    public void MessageReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
        lock (this)
        {
            if (sender is not MidiIn midiIn || msg.MidiEvent == null)
                return;

            // Skip messages from devices that are being controlled by compatible MIDI devices
            // (when in control mode, not passthrough mode)
            if (MidiConnectionManager.IsDeviceInControlMode(midiIn))
                return;

            MidiSignal newSignal = null;

            var device = MidiConnectionManager.GetDescriptionForMidiIn(midiIn);

            switch (msg.MidiEvent)
            {
                case ControlChangeEvent controlEvent:
                {
                    if (_printLogMessages)
                        Log.Debug($"{device}/{controlEvent}", this);

                    if (!UseControlRange)
                    {
                        newSignal = new MidiSignal()
                                        {
                                            Channel = controlEvent.Channel,
                                            ControllerId = (int)controlEvent.Controller,
                                            ControllerValue = controlEvent.ControllerValue,
                                            EventType = MidiEventTypes.ControllerChanges,
                                        };
                    }

                    break;
                }
                case NoteEvent noteEvent:
                    switch (noteEvent.CommandCode)
                    {
                        case MidiCommandCode.NoteOn:
                        {
                            if (_printLogMessages)
                                Log.Debug($"{device}/{noteEvent}  ControlValue :{noteEvent.NoteNumber}", this);

                            newSignal = new MidiSignal()
                                            {
                                                Channel = noteEvent.Channel,
                                                ControllerId = noteEvent.NoteNumber,
                                                ControllerValue = noteEvent.Velocity,
                                                EventType = MidiEventTypes.Notes,
                                            };
                            break;
                        }
                        case MidiCommandCode.NoteOff:
                            newSignal = new MidiSignal()
                                            {
                                                Channel = noteEvent.Channel,
                                                ControllerId = noteEvent.NoteNumber,
                                                ControllerValue = 0,
                                                EventType = MidiEventTypes.Notes,
                                            };
                            break;

                    }

                    break;
                case PitchWheelChangeEvent midiEvent:
                    newSignal = new MidiSignal()
                                    {
                                        Channel = midiEvent.Channel,
                                        ControllerId = 10000+(int)midiEvent.CommandCode,
                                        ControllerValue = midiEvent.Pitch,
                                        EventType = MidiEventTypes.MidiEvent,
                                    };
                    Log.Debug("Pitch " + midiEvent.Pitch);
                    break;
                    
                case PatchChangeEvent patchChangeEvent:
                    newSignal = new MidiSignal()
                                    {
                                        Channel = patchChangeEvent.Channel,
                                        ControllerId = 10000+(int)patchChangeEvent.CommandCode,
                                        ControllerValue = patchChangeEvent.Patch,
                                        EventType = MidiEventTypes.MidiEvent,
                                    };
                    break;
                    
                case ChannelAfterTouchEvent afterTouchEvent:
                    newSignal = new MidiSignal()
                                    {
                                        Channel = afterTouchEvent.Channel,
                                        ControllerId = 10000+(int)afterTouchEvent.CommandCode,
                                        ControllerValue = afterTouchEvent.AfterTouchPressure,
                                        EventType = MidiEventTypes.MidiEvent,
                                    };
                    break;
            }

            if (!_teachingActive && msg.MidiEvent.CommandCode == MidiCommandCode.TimingClock)
            {
                _timingMsgCount++;
                if (_trainedEventType == MidiEventTypes.MidiTime)
                {
                    newSignal = new MidiSignal()
                                    {
                                        Channel = msg.MidiEvent.Channel,
                                        ControllerId = 0,
                                        ControllerValue = _timingMsgCount,
                                        EventType = MidiEventTypes.MidiTime,
                                    };                    
                }                    
            }

            if (newSignal == null)
                return;

            var matchesDevice = string.IsNullOrEmpty(_trainedDeviceName) || device.ProductName == _trainedDeviceName;
            var matchesChannel = _trainedChannel < 0 || newSignal.Channel == _trainedChannel;

            var matchesSingleController = _trainedControllerId < 0 || newSignal.ControllerId == _trainedControllerId;
            var matchesControlRange = _trainedControllerId < 0 ||
                                      (newSignal.ControllerId >= _controlRange.Width && newSignal.ControllerId <= _controlRange.Height);

            var matchesController = UseControlRange
                                        ? matchesControlRange
                                        : matchesSingleController;

            var matchesEventType = _trainedEventType == MidiEventTypes.All || _trainedEventType == newSignal.EventType;

            if (_teachingActive || (matchesDevice && matchesChannel && matchesController && matchesEventType))
            {
                _lastMatchingSignals.Add(newSignal);
                _lastMessageDevice = device;
                _isDefaultValue = false;
                FlagAsDirty();
            }
        }
    }

    public void OnSettingsChanged()
    {
        Result.DirtyFlag.Invalidate();
        Range.DirtyFlag.Invalidate();
        WasHit.DirtyFlag.Invalidate();
    }
        
    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_warningMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Error;
    }

    public string GetStatusMessage()
    {
        return _warningMessage;
    }

    private string _warningMessage;
        

    private Int2 _controlRange;
    private bool UseControlRange => _controlRange.Width > 0 || _controlRange.Height > 0;
    private List<float> _valuesForControlRange;

    private class MidiSignal
    {
        public int Channel;
        public int ControllerId;
        public int ControllerValue;
        public MidiEventTypes EventType;
    }

    public double LastMessageTime; // used for OpUi

    private bool _printLogMessages;
    private bool _isDefaultValue = true;
    private bool _oldTeachTrigger;
    private bool _teachingActive;
    private string _trainedDeviceName;
    private int _trainedChannel = -1;
    private int _trainedControllerId = -1;
    private MidiEventTypes _trainedEventType;
    private readonly List<MidiSignal> _lastMatchingSignals = new(10);
    private MidiInCapabilities _lastMessageDevice;

    private float _currentControllerValue;
    private float _dampedOutputValue;
    private int _currentControllerId;
    private int _timingMsgCount;

    private enum MidiEventTypes
    {
        All,
        Notes,
        ControllerChanges,
        MidiTime,
        MidiEvent,
    }
        
    [Input(Guid = "AAD1E576-F144-423F-83B5-5694B1119C23")]
    public readonly InputSlot<Vector2> OutputRange = new();

    [Input(Guid = "4636D6CF-8233-4281-8840-5BA079B5F1A6")]
    public readonly InputSlot<float> DefaultOutputValue = new();

    [Input(Guid = "CA3CE08D-6A19-4AD5-9435-08B050753311")]
    public readonly InputSlot<float> Damping = new();

    [Input(Guid = "7C681EE6-D071-4284-8585-1C3E03A089EA")]
    public readonly InputSlot<bool> TeachTrigger = new();
        
    [Input(Guid = "044168EB-791C-405F-867F-3D5702924165", MappedType = typeof(MidiEventTypes))]
    public readonly InputSlot<int> EventType = new();

    [Input(Guid = "23C34F4C-4BA3-4834-8D51-3E3909751F84")]
    public readonly InputSlot<string> Device = new();


    [Input(Guid = "9B0D32DE-C53C-4DF6-8B29-5E68A5A9C5F9")]
    public readonly InputSlot<int> Channel = new();

    [Input(Guid = "DF81B7B3-F39E-4E5D-8B97-F29DD576A76D")]
    public readonly InputSlot<int> Control = new();

    [Input(Guid = "F650985F-00A7-452A-B3E4-69A8E9A78C3F")]
    public readonly InputSlot<Int2> ControlRange = new();

    [Input(Guid = "6C15E743-9A70-47E7-A0A4-75636817E441")]
    public readonly InputSlot<bool> PrintLogMessages = new();
        
    [Input(Guid = "AC35E75A-BEC5-497C-9C68-6B809B12CD8B")]
    public readonly InputSlot<bool> ResetToDefaultTrigger = new();


}