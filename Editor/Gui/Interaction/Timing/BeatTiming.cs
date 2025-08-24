#nullable enable
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.IO;

namespace T3.Editor.Gui.Interaction.Timing;

/// <summary>
/// A helper to provide continuous a BeatTime for live situations.
/// The timing can be driven by BPM detection or beat tapping.
///
/// Its result is used by <see cref="BeatTimingPlayback"/> to drive playback. 
/// </summary>
internal static class BeatTiming
{
    internal static double BeatTime { get; private set; }
    internal static float Bpm => (float)(60f / _beatDuration);

    internal static void SetBpmRate(float bpm)
    {
        _beatDuration = 60f / bpm;
        _tapTimes.Clear();
    }

    internal static void TriggerSyncTap() => _tapTriggeredLastFrame = true;
    internal static void TriggerResyncMeasure() => _syncMeasureTriggeredLastFrame = true;

    internal static void Update()
    {
        var playback = Playback.Current;
        var playbackSettings = playback.Settings;
        if (playbackSettings == null)
        {
            Log.Warning("Can't BeatTiming.Update() without playback settings");   
            return;
        }
        
        var runTime = Playback.RunTimeInSecs;
        var distanceToMeasure = 1 - (float)Math.Abs((BeatTime % 4) / 4 - 0.5) * 2;
        BeatTimingDetails.DistanceToMeasure = distanceToMeasure;

        var distanceToBar = 1 - (float)Math.Abs((BeatTime % 1) / 1 - 0.5) * 2;
        BeatTimingDetails.DistanceToBeat = distanceToBar;

        var tappedBeatSync = _tapTriggeredLastFrame;
        _tapTriggeredLastFrame = false;

        var tappedMeasureSync = _syncMeasureTriggeredLastFrame;
        _syncMeasureTriggeredLastFrame = false;
        if (tappedMeasureSync)
        {
            BeatSynchronizer.Resync(Bpm);
            _resynced = true;
        }

        // Fix BPM if completely out of control
        if (double.IsNaN(Bpm) || Bpm < 20 || Bpm > 600)
        {
            Log.Warning($"BPM {Bpm:0.0} out of range. Reverting to 120");
            _beatDuration = 0.5; // 120bpm
            _phaseDelta = 0;
            _syncMeasureOffset = 0;
            _tapTimes.Clear();
            _resyncTimes.Clear();
        }

        ProcessBeatTaps();
        ProcessMeasureSyncTaps();

        AdvanceBeatTime();
        UpdateDebugData();
        return;

        //--------------------------------------------------------


        void ProcessBeatTaps()
        {
            if (!tappedBeatSync)
                return;

            _resynced = false;

            BeatTimingDetails.LastTapDistanceToBeat = (float)GetDeltaToSync(runTime);

            AppendRunTimeToQueue(_tapTimes, MaxTapsCount, 2 * MeasureDuration);
            if (_tapTimes.Count < 4)
            {
                _phaseDelta = 0;
                return;
            }

            var lastTapTime = _tapTimes[^1];
            if (runTime - lastTapTime > 4)
            {
                _phaseDelta = 0;
                _tapTimes.Clear();
                return;
            }

            var durationSum = 0.0;
            var phaseOffsetSum = 0.0;
            var lastT = 0.0;

            for (var tapIndex = 0; tapIndex < _tapTimes.Count; tapIndex++)
            {
                var time = _tapTimes[tapIndex];
                var dt = time - lastT;

                lastT = time;
                if (tapIndex == 0)
                    continue;

                phaseOffsetSum += GetDeltaToSync(time);
                durationSum += dt;
            }

            var stepCount = _tapTimes.Count - 1;
            _beatDuration = durationSum / stepCount;
            _phaseDelta = phaseOffsetSum / stepCount;
        }

        void ProcessMeasureSyncTaps()
        {
            if (!tappedMeasureSync)
                return;

            if (distanceToMeasure > 0.1)
            {
                _resyncTimes.Clear();
                _resyncTimes.Add(runTime);
                Log.Debug($"Skipping Sync as tap {distanceToBar:0.00} < {Threshold:0.00}");
            }
            else
            {
                AppendRunTimeToQueue(_resyncTimes, 8, 8.5 * MeasureDuration);
                if (_resyncTimes.Count > 1)
                {
                    var totalMeasureDuration = runTime - _resyncTimes[0];
                    var totalMeasureCount = Math.Round(totalMeasureDuration / MeasureDuration);

                    var originalBpm = Bpm;
                    _beatDuration = totalMeasureDuration / totalMeasureCount / BeatsPerMeasure;
                    Log.Debug($"Refining BPM rate {originalBpm:0.00} -> {Bpm:0.00}.  (  over {totalMeasureDuration:0.00s}s)");
                }
            }

            _syncMeasureOffset = -(runTime - _measureStartTime) / MeasureDuration;
        }

        void AdvanceBeatTime()
        {
            var timeInMeasure = runTime - _measureStartTime;
            if (timeInMeasure > MeasureDuration)
            {
                _measureCount++;
                _measureStartTime += MeasureDuration;
            }
            
            if (playback.Settings.EnableAudioBeatLocking && _resynced)
            {
                BeatTime = _barTimeAverage.UpdateAndCompute(BeatSynchronizer.BarProgress) 
                           + playback.BarsFromSeconds(playbackSettings.BeatLockAudioOffsetSec);
                
                _beatDuration =   (float)(60f / BeatSynchronizer.CurrentBpm);
            }
            else
            {
                var tInMeasure = (runTime - _measureStartTime) / MeasureDuration;
                BeatTime = (_measureCount + tInMeasure + _syncMeasureOffset) * BeatsPerBar;
            }
        }

        
        void UpdateDebugData()
        {
            BeatTimingDetails.WasResyncTriggered = tappedMeasureSync ? 1f : 0f;
            BeatTimingDetails.WasTapTriggered = tappedBeatSync ? 1f : 0f;

            BeatTimingDetails.Bpm = (float)Bpm;
            BeatTimingDetails.SyncMeasureOffset = (float)_syncMeasureOffset;
            BeatTimingDetails.BeatTime = (float)BeatTime;
            BeatTimingDetails.LastPhaseOffset = (float)_phaseDelta;
            BeatTimingDetails.MeasureDuration = (float)MeasureDuration;
        }

        void AppendRunTimeToQueue(IList<double> list, int maxLength, double maxDuration)
        {
            while (list.Count > maxLength || list.Count > 1 && runTime - list[0] > maxDuration)
            {
                list.RemoveAt(0);
            }

            list.Add(runTime);
        }
    }

    private static double GetDeltaToSync(double time)
    {
        var timeInMeasure = time - _measureStartTime;
        var beatCount = Math.Round(Math.Abs(timeInMeasure) / _beatDuration);
        return timeInMeasure - _beatDuration * beatCount;
    }
    
    private static bool _resynced;

    private static double MeasureDuration => _beatDuration * BeatsPerMeasure;
    private const int MaxTapsCount = 16;
    private static readonly List<double> _tapTimes = new(MaxTapsCount + 1);
    private static readonly List<double> _resyncTimes = new(MaxTapsCount + 1);

    private static int BeatsPerMeasure => BeatsPerBar * BarsPerMeasure;
    private static int BarsPerMeasure { get; set; } = 4;
    private static int BeatsPerBar { get; set; } = 4;

    private static double _beatDuration = 1;

    private static double _phaseDelta;
    private static double _measureStartTime;
    private static double _measureCount;
    private static double _syncMeasureOffset;

    private static bool _syncMeasureTriggeredLastFrame;
    private static bool _tapTriggeredLastFrame;

    private const double Threshold = 0.3;

    private static readonly SlidingAverage _barTimeAverage = new(10);
}