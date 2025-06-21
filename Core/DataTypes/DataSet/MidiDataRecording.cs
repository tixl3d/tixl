using System;
using System.Collections.Generic;
using NAudio.Midi;
using T3.Core.Animation;
using T3.Core.DataTypes.DataSet;
using T3.Core.Model;

namespace Operators.Utils.Recording;

/// <summary>
/// This is a stub for an implementation of midi signal recording
/// - These recordings are intended for later playback so that MidiConsumers would receive the
///   signals and replay them like live signals. For this to work...
///   - MidiInput and MidiStream recorder would need to share the same MidiEvent definition (maybe different from NAudio.MidiEvent)
///   - Handle MidiEvent should not rely on the MidiIn class to avoid double lookup of device description.
/// </summary>
public class MidiDataRecording : MidiConnectionManager.IMidiConsumer
{
    //public readonly DataSet DataSet = new();
    public double LastEventTime = 0;

    private DataSet _dataSet;

    public MidiDataRecording(DataSet dataSet)
    {
        _dataSet = dataSet;
        MidiConnectionManager.RegisterConsumer(this);
    }

    // public void Reset()
    // {
    //     _dataSet.Clear();
    //     _channelsByHash.Clear();
    // }

    void MidiConnectionManager.IMidiConsumer.MessageReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
        if (sender is not MidiIn midiIn || msg.MidiEvent == null || TypeNameRegistry.Entries.Values.Count == 0)
            return;

        if (msg.MidiEvent.CommandCode == MidiCommandCode.AutoSensing)
            return;

        LastEventTime = Playback.RunTimeInSecs;

        var device = MidiConnectionManager.GetDescriptionForMidiIn(midiIn);
        var deviceName = (device.ProductName
                          + (device.ProductId is not (0 or 65535)
                                 ? device.ProductId.ToString()
                                 : string.Empty)).Replace("/", "_");

        var someTime = Playback.RunTimeInSecs; // (float)msg.MidiEvent.AbsoluteTime;
        switch (msg.MidiEvent)
        {
            case NoteEvent midiNoteEvent:
                var noteChannel = FindOrCreateNoteChannel(deviceName, midiNoteEvent);
                var lastNote = noteChannel.GetLastEvent() as DataIntervalEvent;

                switch (msg.MidiEvent.CommandCode)
                {
                    case MidiCommandCode.NoteOff:
                        lastNote?.Finish((float)someTime);
                        break;

                    case MidiCommandCode.NoteOn:
                    {
                        if (lastNote != null && lastNote.IsUnfinished)
                        {
                            lastNote.Finish((float)someTime);
                            if (midiNoteEvent.Velocity == 0)
                                break;
                        }

                        noteChannel.Events.Add(new DataIntervalEvent()
                                                   {
                                                       Time = someTime,
                                                       EndTime = Double.PositiveInfinity,
                                                       TimeCode = someTime,
                                                       Value = (float)midiNoteEvent.Velocity,
                                                   });
                        break;
                    }
                }

                break;

            case ControlChangeEvent controlChangeEvent:
                FindOrCreateControlChangeChannel(deviceName, controlChangeEvent)
                   .Events
                   .Add(new DataEvent()
                            {
                                Time = someTime,
                                TimeCode = someTime,
                                Value = (float)controlChangeEvent.ControllerValue,
                            });
                break;

            case PitchWheelChangeEvent pitchWheelChangeEvent:
                FindOrCreatePitchWheelChangeChannel(deviceName, pitchWheelChangeEvent)
                   .Events
                   .Add(new DataEvent()
                            {
                                Time = someTime,
                                TimeCode = someTime,
                                Value = (float)pitchWheelChangeEvent.Pitch,
                            });
                break;

            case ChannelAfterTouchEvent channelAfterTouchEvent:
                FindOrCreateChannelAfterTouchChannel(deviceName, channelAfterTouchEvent)
                   .Events
                   .Add(new DataEvent()
                            {
                                Time = someTime,
                                TimeCode = someTime,
                                Value = (float)channelAfterTouchEvent.AfterTouchPressure,
                            });
                break;
        }
    }

    private DataChannel FindOrCreateChannelAfterTouchChannel(string deviceName, ChannelAfterTouchEvent channelAfterTouchEvent)
    {
        var hash = channelAfterTouchEvent.Channel;
        hash = hash * 31 + deviceName.GetHashCode();

        if (_channelsByHash.TryGetValue(hash, out var channel))
        {
            return channel;
        }

        var newChannel = new DataChannel(typeof(float))
                             {
                                 Path = new List<string>
                                            {
                                                MidiNamespacePrefix,
                                                deviceName,
                                                ChannelPathPrefix + channelAfterTouchEvent.Channel.ToString(),
                                                "CP"
                                            }
                             };
        _channelsByHash[hash] = newChannel;
        _dataSet.Channels.Add(newChannel);
        return newChannel;
    }

    private DataChannel FindOrCreatePitchWheelChangeChannel(string deviceName, PitchWheelChangeEvent pitchWheelChangeEvent)
    {
        var hash = pitchWheelChangeEvent.Channel;
        hash = hash * 31 + deviceName.GetHashCode();

        if (_channelsByHash.TryGetValue(hash, out var channel))
        {
            return channel;
        }

        var newChannel = new DataChannel(typeof(float))
                             {
                                 Path = new List<string>
                                            {
                                                MidiNamespacePrefix,
                                                deviceName,
                                                ChannelPathPrefix + pitchWheelChangeEvent.Channel.ToString(),
                                                "PB"
                                            }
                             };
        _channelsByHash[hash] = newChannel;
        _dataSet.Channels.Add(newChannel);
        return newChannel;
    }

    private DataChannel FindOrCreateControlChangeChannel(string deviceName, ControlChangeEvent controlChangeEvent)
    {
        var hash = (byte)controlChangeEvent.Controller << 16 + controlChangeEvent.Channel;
        hash = hash * 31 + deviceName.GetHashCode();

        if (_channelsByHash.TryGetValue(hash, out var channel))
        {
            return channel;
        }

        var newChannel = new DataChannel(typeof(float))
                             {
                                 Path = new List<string>
                                            {
                                                MidiNamespacePrefix,
                                                deviceName,
                                                ChannelPathPrefix + controlChangeEvent.Channel.ToString(),
                                                "CC" + (int)controlChangeEvent.Controller
                                            }
                             };
        _channelsByHash[hash] = newChannel;
        _dataSet.Channels.Add(newChannel);
        return newChannel;
    }

    private DataChannel FindOrCreateNoteChannel(string deviceName, NoteEvent noteEvent)
    {
        var hash = (byte)noteEvent.NoteNumber << 16 + noteEvent.Channel;
        hash = hash * 31 + deviceName.GetHashCode();

        if (_channelsByHash.TryGetValue(hash, out var channel))
        {
            return channel;
        }

        var newChannel = new DataChannel(typeof(float))
                             {
                                 Path = new List<string>
                                            {
                                                MidiNamespacePrefix,
                                                deviceName,
                                                ChannelPathPrefix + noteEvent.Channel.ToString(),
                                                "N" + noteEvent.NoteNumber
                                            },
                             };
        _channelsByHash[hash] = newChannel;
        _dataSet.Channels.Add(newChannel);
        return newChannel;
    }

    void MidiConnectionManager.IMidiConsumer.ErrorReceivedHandler(object sender, MidiInMessageEventArgs msg)
    {
    }

    void MidiConnectionManager.IMidiConsumer.OnSettingsChanged()
    {
    }

    private const string ChannelPathPrefix = "Ch";
    private const string MidiNamespacePrefix = "Midi";
    private readonly Dictionary<int, DataChannel> _channelsByHash = new();
}