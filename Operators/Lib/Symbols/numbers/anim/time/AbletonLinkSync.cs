using T3.Core.Animation;
using T3.Core.Utils;

namespace Lib.numbers.anim.time;

[Guid("31ab98ec-5e79-4667-9a85-2fb168f41fa1")]
[ExportDependencies("AbletonLink.deps.json", "AbletonLinkDLL.dll", "AbletonLink.dll")]
internal sealed class AbletonLinkSync : Instance<AbletonLinkSync>, IStatusProvider
{
    [Output(Guid = "e1cfd42c-81fa-4820-91df-f1bad27b3a7f", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Result = new();

    [Output(Guid = "D6E453E6-1D3F-4765-A427-DD9967BFBC34", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<float> Tempo = new();

    [Output(Guid = "DEAB643C-F608-4DA9-A1C2-298449D17BC0", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly Slot<bool> IsConnected = new();
        
    public AbletonLinkSync()
    {
        TryInitialize();
        Result.UpdateAction += Update;
        Tempo.UpdateAction += Update;
    }

    private void Update(EvaluationContext context)
    {
        var isConnected = _nativeLinkInstance != IntPtr.Zero;
        IsConnected.Value = isConnected;
            
        if (!isConnected)
            return;
            
        if (MathUtils.WasTriggered(TriggerStartPlaying.GetValue(context), ref _triggerStart))
        {
            StartPlaying();
            TriggerStartPlaying.SetTypedInputValue(false);
        }

        if (MathUtils.WasTriggered(TriggerStopPlaying.GetValue(context), ref _triggerStop))
        {
            StopPlaying();
            TriggerStopPlaying.SetTypedInputValue(false);
        }

        var needsReconnected = false;
        if (MathUtils.WasTriggered(TriggerReconnect.GetValue(context), ref _triggerReconnected))
        {
            needsReconnected = true;
            TriggerReconnect.SetTypedInputValue(false);
        }

        UpdateLink(out var beat, out var phase, out var tempo, out var quantum, out var time, out var peerCount);

        if (_startMeasure == 0)
        {
            _startMeasure = Math.Floor(beat / quantum) * quantum;
            _startMeasure = 0;
        }

        var pauseIfDisconnected = PauseIfDisconnected.GetValue(context);
        var pauseResults = pauseIfDisconnected && peerCount == 0;

        if (!pauseResults)
        {
            Tempo.Value = (float)tempo;
            Result.Value = OutputType.GetEnumValue<ReturnTypes>(context) switch
                               {
                                   ReturnTypes.Bars    => (float)(beat / quantum - _startMeasure),
                                   ReturnTypes.Phase   => (float)phase,
                                   ReturnTypes.Beats   => (float)(beat - _startMeasure),
                                   ReturnTypes.Time    => (float)(time / 1000),
                                   ReturnTypes.Quantum => (float)quantum,
                                   _                   => 0
                               };
        }

        if (peerCount != _peerCount)
        {
            Log.Debug("Number of Ableton Link Peers changed to " + peerCount);
            _peerCount = peerCount;
        }

        _lastErrorMessage = peerCount == 0 ? "No peers connected" : null;

        if (AutoConnect.GetValue(context) && peerCount == 0 && Playback.RunTimeInSecs - _lastReconnectTime > 3)
        {
            needsReconnected = true;
        }

        if (needsReconnected)
        {
            _lastReconnectTime = Playback.RunTimeInSecs;
            Destroy();
            TryInitialize();
        }

        Tempo.DirtyFlag.Clear();
        Result.DirtyFlag.Clear();
    }

    private double _startMeasure;
    private double _lastReconnectTime;

    private void TryInitialize()
    {
        lock (_lock)
        {
            if (_initialized)
                return;

            // Log.Debug("Initializing AbletonLink...");
            try
            {
                _nativeLinkInstance = CreateAbletonLink();
                Setup(InitialTempo);
                EnableStartStopSync(true);
            }
            catch (Exception e)
            {
                var message = "Failed to initialize AbletonLink. Probably can't find dll. Did you run install.bat? " + e.Message;
                Log.Warning(message);
                _lastErrorMessage = message;
            }
            _initialized = true;
        }
    }

    private void Destroy()
    {
        lock (_lock)
        {
            if (!_initialized)
                return;

            if (_nativeLinkInstance == IntPtr.Zero)
                return;

            // Log.Debug("Destroying Ableton Link...");
            DestroyAbletonLink(_nativeLinkInstance);
            _nativeLinkInstance = IntPtr.Zero;
            _initialized = false;
        }
    }

    private static int _peerCount;

    [Input(Guid = "CD977DBC-3340-4542-8E0F-01BDE3882D6A", MappedType = typeof(ReturnTypes))]
    public readonly InputSlot<int> OutputType = new();

    [Input(Guid = "3DC1FC6B-D17F-4939-A1E6-6D3DB042AF36")]
    public readonly InputSlot<bool> TriggerStartPlaying = new();

    [Input(Guid = "BF2CA9E8-6365-453F-B8C3-2428CCD9D23D")]
    public readonly InputSlot<bool> TriggerStopPlaying = new();

    [Input(Guid = "F4E75E75-5113-4F22-A1DD-B897A5745F1D")]
    public readonly InputSlot<bool> TriggerReconnect = new();

    [Input(Guid = "4DA6EBCC-0312-4846-88B0-A0682A59F97F")]
    public readonly InputSlot<bool> AutoConnect = new();

    [Input(Guid = "ECF5D5AB-BB9F-4C48-9B33-8827E94DD286")]
    public readonly InputSlot<bool> PauseIfDisconnected = new();

    #region use wrapper
    [DllImport("AbletonLinkDLL")]
    private static extern IntPtr CreateAbletonLink();

    [DllImport("AbletonLinkDLL")]
    private static extern void DestroyAbletonLink(IntPtr ptr);

    protected override void Dispose(bool isDisposing)
    {
        if (!isDisposing)
            return;

        Destroy();
        // Log.Debug("Disposing AbletonLink");
        // if (_nativeLinkInstance != IntPtr.Zero)
        // {
        //     DestroyAbletonLink(_nativeLinkInstance);
        //     _nativeLinkInstance = IntPtr.Zero;
        // }
    }

    [DllImport("AbletonLinkDLL")]
    private static extern void setup(IntPtr ptr, double bpm);

    private static void Setup(double bpm)
    {
        setup(_nativeLinkInstance, bpm);
    }

    [DllImport("AbletonLinkDLL")]
    private static extern void setTempo(IntPtr ptr, double bpm);

    [DllImport("AbletonLinkDLL")]
    private static extern double tempo(IntPtr ptr);

    public double LinkTempo { get => tempo(_nativeLinkInstance); set => setTempo(_nativeLinkInstance, value); }

    [DllImport("AbletonLinkDLL")]
    private static extern void setQuantum(IntPtr ptr, double quantum);

    [DllImport("AbletonLinkDLL")]
    private static extern double quantum(IntPtr ptr);

    public double Quantum { get => quantum(_nativeLinkInstance); set => setQuantum(_nativeLinkInstance, value); }

    [DllImport("AbletonLinkDLL")]
    private static extern void forceBeatAtTime(IntPtr ptr, double beat);

    public void ForceBeatAtTime(double beat)
    {
        forceBeatAtTime(_nativeLinkInstance, beat);
    }

    [DllImport("AbletonLinkDLL")]
    private static extern void requestBeatAtTime(IntPtr ptr, double beat);

    public void RequestBeatAtTime(double beat)
    {
        requestBeatAtTime(_nativeLinkInstance, beat);
    }

    [DllImport("AbletonLinkDLL")]
    private static extern void enable(IntPtr ptr, bool bEnable);

    [DllImport("AbletonLinkDLL")]
    private static extern bool isEnabled(IntPtr ptr);

    public static bool Enabled { get => isEnabled(_nativeLinkInstance); set => enable(_nativeLinkInstance, value); }

    [DllImport("AbletonLinkDLL")]
    private static extern void enableStartStopSync(IntPtr ptr, bool bEnable);

    public static void EnableStartStopSync(bool enable)
    {
        enableStartStopSync(_nativeLinkInstance, enable);
    }

    [DllImport("AbletonLinkDLL")]
    private static extern void startPlaying(IntPtr ptr);

    public static void StartPlaying()
    {
        startPlaying(_nativeLinkInstance);
    }

    [DllImport("AbletonLinkDLL")]
    private static extern void stopPlaying(IntPtr ptr);

    public static void StopPlaying()
    {
        stopPlaying(_nativeLinkInstance);
    }

    [DllImport("AbletonLinkDLL")]
    private static extern bool isPlaying(IntPtr ptr);

    public bool IsPlaying => isPlaying(_nativeLinkInstance);

    [DllImport("AbletonLinkDLL")]
    private static extern int numPeers(IntPtr ptr);

    private int NumPeers => numPeers(_nativeLinkInstance);

    [DllImport("AbletonLinkDLL")]
    private static extern void update(IntPtr ptr, out double rbeat, out double rphase, out double rtempo, out double rquantum, out double rtime,
                                      out int rnumPeers);

    public static void UpdateLink(out double beat, out double phase, out double tempo, out double quantum, out double time, out int numPeers)
    {
        update(_nativeLinkInstance, out beat, out phase, out tempo, out quantum, out time, out numPeers);
    }
    #endregion

    private enum ReturnTypes
    {
        Bars,
        Phase,
        Beats,
        Time,
        Quantum,
    }

    // private static volatile AbletonLink singletonInstance;
    private static IntPtr _nativeLinkInstance = IntPtr.Zero;
    private bool _triggerStart;
    private bool _triggerStop;
    private const double InitialTempo = 120.0;

    private static bool _initialized;
    private readonly object _lock = new();
    private bool _triggerReconnected;
    private string _lastErrorMessage = null;

    public IStatusProvider.StatusLevel GetStatusLevel()
    {
        return string.IsNullOrEmpty(_lastErrorMessage) ? IStatusProvider.StatusLevel.Success : IStatusProvider.StatusLevel.Warning;
    }

    public string GetStatusMessage()
    {
        return _lastErrorMessage;
    }
}