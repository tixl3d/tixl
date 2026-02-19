using System;

namespace T3.Core.Audio;

/// <summary>
/// Shared ADSR envelope calculator that can be used by any operator.
/// This is a pure calculation class with no dependencies on the operator system.
/// 
/// Supports two modes of operation:
/// 1. Frame-based: Call Update() once per frame with delta time (for UI/visual use)
/// 2. Sample-based: Call UpdateSample() per audio sample (for audio thread use)
/// </summary>
public sealed class AdsrCalculator
{
    public enum Stage
    {
        Idle = 0,
        Attack = 1,
        Decay = 2,
        Sustain = 3,
        Release = 4
    }

    public enum TriggerMode
    {
        Gate = 0,
        Trigger = 1
    }


    // Current state
    public float Value { get; private set; }
    public Stage CurrentStage { get; private set; } = Stage.Idle;
    public bool IsActive => CurrentStage != Stage.Idle;

    // Parameters (can be updated at any time)
    private float _attackTime = 0.01f;
    private float _decayTime = 0.1f;
    private float _sustainLevel = 0.7f;
    private float _releaseTime = 0.3f;
    private TriggerMode _mode = TriggerMode.Gate;
    private float _duration = float.MaxValue;
    private int _sampleRate = 48000;

    // Internal state for frame-based updates
    private bool _previousGate;
    private double _lastTime;
    private float _stageTime;
    private float _totalTime;

    // Internal state for sample-based updates
    private long _stageSampleCount;
    private long _totalSamplesPlayed;
    private long _durationSamples;

    // Shared state
    private double _releaseStartValue;

    // Trigger signals for sample-based mode
    private volatile int _triggerAttackSignal;
    private volatile int _triggerReleaseSignal;

    /// <summary>
    /// Sets the ADSR parameters. Thread-safe for sample-based mode.
    /// </summary>
    public void SetParameters(float attack, float decay, float sustain, float release)
    {
        _attackTime = Math.Max(0.001f, attack);
        _decayTime = Math.Max(0.001f, decay);
        _sustainLevel = Math.Clamp(sustain, 0f, 1f);
        _releaseTime = Math.Max(0.001f, release);
    }

    /// <summary>
    /// Sets the trigger mode. Thread-safe for sample-based mode.
    /// </summary>
    public void SetMode(TriggerMode mode)
    {
        _mode = mode;
    }

    /// <summary>
    /// Sets the duration for Trigger mode. Thread-safe for sample-based mode.
    /// </summary>
    public void SetDuration(float duration)
    {
        _duration = duration > 0 ? duration : float.MaxValue;
    }

    /// <summary>
    /// Sets the sample rate for sample-based updates.
    /// </summary>
    public void SetSampleRate(int sampleRate)
    {
        _sampleRate = Math.Max(1, sampleRate);
    }

    /// <summary>
    /// Triggers the attack phase. Thread-safe for use from UI thread.
    /// </summary>
    public void TriggerAttack()
    {
        System.Threading.Interlocked.Exchange(ref _triggerAttackSignal, 1);
    }

    /// <summary>
    /// Triggers the release phase. Thread-safe for use from UI thread.
    /// </summary>
    public void TriggerRelease()
    {
        System.Threading.Interlocked.Exchange(ref _triggerReleaseSignal, 1);
    }

    /// <summary>
    /// Updates the envelope for one audio sample. Call this from the audio thread.
    /// Returns the current envelope value (0-1).
    /// </summary>
    public float UpdateSample()
    {
        // Check for trigger signals (thread-safe)
        if (System.Threading.Interlocked.Exchange(ref _triggerAttackSignal, 0) == 1)
        {
            CurrentStage = Stage.Attack;
            _stageSampleCount = 0;
            _totalSamplesPlayed = 0;
            _durationSamples = _duration >= float.MaxValue / 2f 
                ? long.MaxValue 
                : (long)(_duration * _sampleRate);
        }

        if (System.Threading.Interlocked.Exchange(ref _triggerReleaseSignal, 0) == 1)
        {
            if (CurrentStage != Stage.Idle && CurrentStage != Stage.Release)
            {
                _releaseStartValue = Value;
                _stageSampleCount = 0;
                CurrentStage = Stage.Release;
            }
        }

        // Convert times to samples
        long attackSamples = (long)(_attackTime * _sampleRate);
        long decaySamples = (long)(_decayTime * _sampleRate);
        long releaseSamples = (long)(_releaseTime * _sampleRate);

        // Check duration limit in Trigger mode
        if (_mode == TriggerMode.Trigger &&
            CurrentStage != Stage.Idle && CurrentStage != Stage.Release &&
            _durationSamples < long.MaxValue &&
            _totalSamplesPlayed >= _durationSamples)
        {
            _releaseStartValue = Value;
            _stageSampleCount = 0;
            CurrentStage = Stage.Release;
        }

        // Calculate envelope value
        switch (CurrentStage)
        {
            case Stage.Idle:
                Value = 0;
                break;

            case Stage.Attack:
                if (_stageSampleCount < attackSamples)
                {
                    Value = (float)_stageSampleCount / attackSamples;
                }
                else
                {
                    Value = 1.0f;
                    _stageSampleCount = 0;
                    CurrentStage = Stage.Decay;
                }
                break;

            case Stage.Decay:
                if (_stageSampleCount < decaySamples)
                {
                    float progress = (float)_stageSampleCount / decaySamples;
                    Value = 1.0f - progress * (1.0f - _sustainLevel);
                }
                else
                {
                    Value = _sustainLevel;
                    _stageSampleCount = 0;
                    CurrentStage = Stage.Sustain;
                }
                break;

            case Stage.Sustain:
                Value = _sustainLevel;
                break;

            case Stage.Release:
                if (_stageSampleCount < releaseSamples)
                {
                    float progress = (float)_stageSampleCount / releaseSamples;
                    Value = (float)(_releaseStartValue * (1.0 - progress));
                }
                else
                {
                    Value = 0;
                    CurrentStage = Stage.Idle;
                    _stageSampleCount = 0;
                }
                break;
        }

        // Update counters
        _stageSampleCount++;
        if (CurrentStage != Stage.Idle && CurrentStage != Stage.Release)
        {
            _totalSamplesPlayed++;
        }

        Value = Math.Clamp(Value, 0f, 1f);
        return Value;
    }

    /// <summary>
    /// Updates the envelope state based on the current gate/trigger input.
    /// Call this once per frame with the current time and parameters (for UI/visual use).
    /// </summary>
    /// <param name="gate">Gate/trigger input (true = on)</param>
    /// <param name="currentTime">Current time in seconds (e.g., context.LocalFxTime)</param>
    /// <param name="attack">Attack time in seconds</param>
    /// <param name="decay">Decay time in seconds</param>
    /// <param name="sustain">Sustain level (0-1)</param>
    /// <param name="release">Release time in seconds</param>
    /// <param name="mode">Trigger mode (Gate or Trigger)</param>
    /// <param name="duration">Duration for Trigger mode (ignored in Gate mode)</param>
    public void Update(bool gate, double currentTime, float attack, float decay, float sustain, float release, TriggerMode mode, float duration = float.MaxValue)
    {
        // Store parameters
        SetParameters(attack, decay, sustain, release);
        _mode = mode;
        _duration = duration > 0 ? duration : float.MaxValue;

        // Detect edges
        var risingEdge = gate && !_previousGate;
        var fallingEdge = !gate && _previousGate;
        _previousGate = gate;

        // Get time delta
        var deltaTime = (float)(currentTime - _lastTime);
        _lastTime = currentTime;

        // Clamp delta time to avoid huge jumps
        if (deltaTime < 0 || deltaTime > 1.0f)
            deltaTime = 0.016f; // ~60fps fallback

        if (mode == TriggerMode.Gate)
        {
            // GATE MODE: Envelope follows the input signal
            if (risingEdge)
            {
                CurrentStage = Stage.Attack;
                _stageTime = 0;
                _releaseStartValue = Value;
            }
            else if (fallingEdge && CurrentStage != Stage.Idle)
            {
                CurrentStage = Stage.Release;
                _stageTime = 0;
                _releaseStartValue = Value;
            }
        }
        else // Trigger mode
        {
            // TRIGGER MODE: Rising edge starts full ADSR cycle
            if (risingEdge)
            {
                CurrentStage = Stage.Attack;
                _stageTime = 0;
                _totalTime = 0;
                _releaseStartValue = Value;
            }

            // Check if duration reached (trigger release)
            if (CurrentStage != Stage.Idle && 
                CurrentStage != Stage.Release &&
                _duration < float.MaxValue &&
                _totalTime >= _duration)
            {
                CurrentStage = Stage.Release;
                _stageTime = 0;
                _releaseStartValue = Value;
            }
        }

        // Update timing
        _stageTime += deltaTime;
        if (CurrentStage != Stage.Release && CurrentStage != Stage.Idle)
        {
            _totalTime += deltaTime;
        }

        // Calculate envelope value based on current stage
        switch (CurrentStage)
        {
            case Stage.Idle:
                Value = 0;
                break;

            case Stage.Attack:
                if (_stageTime < _attackTime)
                {
                    Value = _stageTime / _attackTime;
                }
                else
                {
                    Value = 1.0f;
                    CurrentStage = Stage.Decay;
                    _stageTime = 0;
                }
                break;

            case Stage.Decay:
                if (_stageTime < _decayTime)
                {
                    var progress = _stageTime / _decayTime;
                    Value = 1.0f - progress * (1.0f - _sustainLevel);
                }
                else
                {
                    Value = _sustainLevel;
                    CurrentStage = Stage.Sustain;
                    _stageTime = 0;
                }
                break;

            case Stage.Sustain:
                Value = _sustainLevel;
                // Stay in sustain until release is triggered
                break;

            case Stage.Release:
                if (_stageTime < _releaseTime)
                {
                    var progress = _stageTime / _releaseTime;
                    Value = (float)(_releaseStartValue * (1.0f - progress));
                }
                else
                {
                    Value = 0;
                    CurrentStage = Stage.Idle;
                    _stageTime = 0;
                }
                break;
        }

        Value = Math.Clamp(Value, 0f, 1f);
    }

    /// <summary>
    /// Manually trigger the release phase (e.g., when gate goes low in gate mode)
    /// For frame-based mode. For sample-based, use TriggerRelease().
    /// </summary>
    public void StartRelease()
    {
        if (CurrentStage != Stage.Idle && CurrentStage != Stage.Release)
        {
            _releaseStartValue = Value;
            _stageTime = 0;
            _stageSampleCount = 0;
            CurrentStage = Stage.Release;
        }
    }

    /// <summary>
    /// Reset the envelope to idle state
    /// </summary>
    public void Reset()
    {
        Value = 0;
        CurrentStage = Stage.Idle;
        _stageTime = 0;
        _totalTime = 0;
        _stageSampleCount = 0;
        _totalSamplesPlayed = 0;
        _releaseStartValue = 0;
        _previousGate = false;
        System.Threading.Interlocked.Exchange(ref _triggerAttackSignal, 0);
        System.Threading.Interlocked.Exchange(ref _triggerReleaseSignal, 0);
    }
}
