# Syncing to DJ software with CLX

[CLX](https://github.com/medcelerate/CLX-Spec) is a network protocol that streams the state of DJ software to visuals
software: where each deck's playhead is, its tempo and beat, which track is loaded, the mixer faders, and the
track's cues, beat grid and waveform. Clockworks Server and the Clockworks Bridge send it for Serato, Traktor,
rekordbox and Pioneer CDJs.

The CLX operators are an extension. Install **CLX** from **Settings → Extensions** (see [Extensions](Extensions.md)).

## Operators

| Operator | What it gives you |
|---|---|
| [ClxDeck] | Playhead, beat position, bars, tempo, pitch and EQ of one deck, about 60 times a second |
| [ClxTrack] | Title, artist and file of the track on a deck, and a trigger when it changes |
| [ClxMixer] | Channel faders, crossfader and the active deck |
| [ClxEvent] | A trigger for events such as `loop_enter`, with their values |
| [ClxCues] | The track's hot cues and memory cues |
| [ClxBeatgrid] | The time of every beat and downbeat in the track |
| [ClxWaveform] | The track's waveform as a texture, in the DJ software's colours |
| [ClxLeader] | The show clock of a leader instance: live deck, position and project |

Decks are numbered from 1.

## Following a deck with the timeline

To run TiXL's timeline in step with a deck, connect [ClxDeck]'s **Bars** to [SetPlaybackTime] with its mode set to
*Continuously*, and its **Bpm** to [SetBpm]. Keyframes placed on bars then line up with the track's beat grid.

LITE senders don't send a playhead in seconds or beats. Use **NormalizedPosition**, which runs from 0 to 1 over the
track.

## Network

CLX is UDP on port 3650. The operators listen for both unicast and the multicast group `239.0.0.1`, and every
5 seconds ask senders to resend track data. All CLX operators on the same port share one connection.

If nothing arrives, the operators show it in their status:

- **Check the sender's target.** A sender streams to one address, plus multicast if that's turned on. In Clockworks
  Server, turn on **Enable Multicast** or set the target to this computer.
- **A Clockworks Viewer on the same computer** can bind the same port, but only one of the two gets each unicast
  packet. Use multicast.
- **macOS** asks for Local Network access the first time; **Windows** asks to allow TiXL through the firewall.
