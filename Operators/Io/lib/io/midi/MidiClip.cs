using NAudio.Midi;


namespace Lib.io.midi;

[Guid("a3ceb788-4055-4556-961b-63b7221f93e7")]
internal sealed class MidiClip : Instance<MidiClip>
{
    [Output(Guid = "04BFDF5C-7D05-469A-89BE-525F27186F69", DirtyFlagTrigger = DirtyFlagTrigger.Animated)]
    public readonly TimeClipSlot<Dict<float>> Values = new();

    [Output(Guid = "C08C4B81-65B0-4FC3-AF46-F06E72838F9D")]
    public readonly Slot<List<string>> ChannelNames = new();

    [Output(Guid = "AADD9189-0086-42D6-AC45-D694270C0252")]
    public readonly Slot<float> DeltaTicksPerQuarterNote = new();

    public MidiClip()
    {
        _initialized = false;
        Values.UpdateAction += Update;
        ChannelNames.UpdateAction += Update;
        DeltaTicksPerQuarterNote.UpdateAction += Update;
    }

    protected override void Dispose(bool isDisposing)
    {
        //if (!isDisposing)
        //    return;
    }

    private void Update(EvaluationContext context)
    {
        try
        {
            if (!_initialized || Filename.DirtyFlag.IsDirty)
            {
                SetupMidiFile(context);
                _channelNames = _channels.Keys.ToList();
                ChannelNames.Value = _channelNames;
            }

            if (_midiEventCollection == null) 
                return;

            _printLogMessages = PrintLogMessages.GetValue(context);

            // Get scaled time range of clip
            var timeRange = Values.TimeClip.TimeRange;
            var sourceRange = Values.TimeClip.SourceRange;

            // Get the time we should be at in the MIDI file according to the timeClip
            var bars = context.LocalTime - timeRange.Start;
            if (Math.Abs(timeRange.End - timeRange.Start) > 0.0001f)
            {
                var rate = (sourceRange.End - sourceRange.Start)
                           / (timeRange.End - timeRange.Start);
                bars *= rate;
            }

            bars += sourceRange.Start;

            // For now: brute-force rewind if we run backwards in time
            if (bars < _lastTimeInBars)
                ClearTracks();

            _lastTimeInBars = bars;

            // Include past events in our response
            var minRange = Math.Min(sourceRange.Start, sourceRange.End);
            var someTrackChanged = false;
            if (bars >= minRange &&
                bars < minRange + Math.Abs(sourceRange.Duration))
            {
                for (var trackIndex = 0; trackIndex < _midiFile.Tracks; trackIndex++)
                {
                    someTrackChanged |= UpdateTrack(trackIndex, bars);
                }
            }

            if (someTrackChanged)
                Values.Value = _channels;
        }
            
        catch (Exception e)
        {
            Log.Debug("Updating MidiClip failed:" + e.Message, this);
        }
    }

        
    private List<string> _channelNames = new();

    private void SetupMidiFile(EvaluationContext context)
    {
        var filename = Filename.GetValue(context);
        if (string.IsNullOrEmpty(filename))
            return;

        if (!TryGetFilePath(filename, out var filePath))
        {
            Log.Error($"Could not find file: {filename}", this);
            return;
        }
            
        // Initialize MIDI file reading, then read all parameters from file
        const bool noStrictMode = false;
        _midiFile = new MidiFile(filePath, noStrictMode);
        _deltaTicksPerQuarterNote = _midiFile.DeltaTicksPerQuarterNote;
        _midiEventCollection = _midiFile.Events;
        ClearTracks();
            
        _timeSignature = _midiFile.Events[0].OfType<TimeSignatureEvent>().FirstOrDefault();

        // Update slots
        DeltaTicksPerQuarterNote.Value = (int)_deltaTicksPerQuarterNote;    // conversion to int is probably bad

        _initialized = true;
    }

    private void ClearTracks()
    {
        _lastTrackEventIndices = Repeat(-1, _midiFile.Tracks).ToList();

        foreach (var k in _channels.Keys)
        {
            _channels[k] = 0;
        }
    }
        
    private bool UpdateTrack(int trackIndex, double time)
    {
        if (trackIndex >= _lastTrackEventIndices.Count ||
            trackIndex >= _midiFile.Events[trackIndex].Count) 
            return false;

        var events = _midiFile.Events[trackIndex];
            
        var lastEventIndex = _lastTrackEventIndices[trackIndex];
        if (lastEventIndex + 1 >= events.Count) 
            return false;

        var valuesChanged = false;
        var timeInTicks = (long)(time * 4 * _deltaTicksPerQuarterNote);
        var nextEventIndex = lastEventIndex + 1;
        while (nextEventIndex < events.Count
               && timeInTicks >= events[nextEventIndex].AbsoluteTime)
        {
            if (_printLogMessages)
            {
                Log.Debug(TimeToBarsBeatsTicks(events[nextEventIndex].AbsoluteTime, _deltaTicksPerQuarterNote, _timeSignature));
            }

            switch (events[nextEventIndex])
            {
                case NoteOnEvent noteOnEvent:
                {
                    var channel = noteOnEvent.Channel;
                    var name = noteOnEvent.NoteName;
                    var value = noteOnEvent.Velocity / 127f;
                    var key = $"/channel{channel}/{name}";
                    _channels[key] = value;
                    valuesChanged = true;

                    if (_printLogMessages)
                        Log.Debug(key + "=" + value);
                    break;
                }
                case NoteEvent noteEvent:
                {
                    var channel = noteEvent.Channel;
                    var name = noteEvent.NoteName;
                    const float value = 0.0f;
                    var key = $"/channel{channel}/{name}";
                    _channels[key] = value;
                    valuesChanged = true;

                    if (_printLogMessages)
                        Log.Debug(key + "=" + value);
                    break;
                }
                case ControlChangeEvent controlChangeEvent:
                {
                    var channel = controlChangeEvent.Channel;
                    var controller = (int)controlChangeEvent.Controller;
                    var value = controlChangeEvent.ControllerValue / 127f;
                    var key = $"/channel{channel}/controller{controller}";
                    _channels[key] = value;
                    valuesChanged = true;

                    if (_printLogMessages)
                        Log.Debug($"{key}={value}", this);
                        
                    break;
                }
            }

            lastEventIndex = nextEventIndex;
            nextEventIndex = lastEventIndex + 1;
        }

        _lastTrackEventIndices[trackIndex] = lastEventIndex;
        return valuesChanged;
    }

    /**
     * From https://github.com/naudio/NAudio/blob/master/Docs/MidiFile.md
     */
    private static string TimeToBarsBeatsTicks(long eventTime, double ticksPerQuarterNote, TimeSignatureEvent timeSignature)
    {
        var beatsPerBar = timeSignature?.Numerator ?? 4;
        var ticksPerBar = timeSignature == null
                              ? ticksPerQuarterNote * 4
                              : (timeSignature.Numerator * ticksPerQuarterNote * 4) / (1 << timeSignature.Denominator);
        var ticksPerBeat = ticksPerBar / beatsPerBar;
        var bar = (eventTime / ticksPerBar);
        var beat = ((eventTime % ticksPerBar) / ticksPerBeat);
        var tick = eventTime % ticksPerBeat;
        return string.Format($"{bar}:{beat}:{tick}");
    }

    // The MIDI file input
    private bool _initialized = false;
    private MidiFile _midiFile = null;
    private MidiEventCollection _midiEventCollection = null;
    private double _deltaTicksPerQuarterNote = 500000.0 / 60;

    // Output data
    private readonly Dict<float> _channels = new(0f);

    // Parsing the file
    private TimeSignatureEvent _timeSignature = null;
    private double _lastTimeInBars = 0f;
    private List<int> _lastTrackEventIndices = null;
    private bool _printLogMessages = false;

    [Input(Guid = "31FE831F-C3BE-4AE3-884B-D2FC4F1754A4")]
    public readonly InputSlot<string> Filename = new();

    [Input(Guid = "8B88C669-7351-4332-9294-9A06A46F45A1")]
    public readonly InputSlot<bool> PrintLogMessages = new();
}