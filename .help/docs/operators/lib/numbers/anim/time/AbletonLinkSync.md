# AbletonLinkSync

*in [Lib.numbers.anim.time](README.md)*

An experimental implementation of the Ableton Link synchronization.

This operator uses the library provided by Ableton to identify available Ableton Link sources and automatically connects to them.
All instances of this operator share the same connection. To align the timing information provided by Link with Tooll's time units, we also provide various output timing formats.

This operator can be combined with [SetPlaybackTime] or [SetBpm]. 

Note: if the connection is cancelled by the host or client, it might be necessary to trigger reconnect.

## Input Parameters
| Name (Relevancy & Type) | Description |
|---|---|
| **OutputType** (Int32) | We attempt to map the incoming timing signals to Tooll's measure units.<br/><br/>- Measure: 1 unit per bar (e.g. per 4 beats)<br/>- Phase: Phase returned by Link<br/>- Beats: Beats returned by Link offset by start measure<br/>- Time: Time returned by Link / 1000<br/>- Quantum: In order to enable the desired bar and loop alignment, an application provides a quantum value to Link that specifies, in beats, the desired unit of phase synchronization. Link guarantees that session participants with the same quantum value will be phase aligned, meaning that if two participants have a 4 beat quantum, beat 3 on one participant’s timeline could correspond to beat 11 on another’s, but not beat 12 (see Link documentation)<br/> |
| **TriggerStartPlaying** (Boolean) | Sends a start signal |
| **TriggerStopPlaying** (Boolean) | Sends a stop signal |
| **TriggerReconnect** (Boolean) | Closes and restarts the connection. This may be necessary if the host cancels the connection. |
| **AutoConnect** (Boolean) | If there are no peers found, we try to reconnect every 3 seconds. |
| **PauseIfDisconnected** (Boolean) | Normally Ableton Link would keep playing even if connection gets lost. If this option is enabled the output will be paused if there are no other peers. |

## Outputs
| Name | Type |
|---|---|
| **Result** | System.Single |
| **Tempo** | System.Single |
| **IsConnected** | System.Boolean |

