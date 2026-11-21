# Boygrouping and show failover

Two features for running a show on more than one computer. Both are experimental and use plain sockets, so they work on Windows, Linux and macOS.

## Boygrouping

The menu bar shows the machine's current **show-control mode** — Boygroup, Failover, Collab or Off — and clicking it switches modes: the three features are mutually exclusive, and entering one tears the others down (leaving sessions). The settings panels configure each mode; starting a session from a panel claims its mode. There are no separate enable switches.

Boygrouping distributes a show to render nodes. Pick a role in **Settings → Boygrouping**: **Master** on the machine that edits the show, **Slave** on every render node — then select the Boygroup mode in the menu bar (or press **Start Server** / **Join** in the panel, which claims the mode). Enabled instances announce themselves on the LAN — there is no address, password or scan step. The master lists the TiXL instances it finds and **Add** invites one to connect.

- **Deploy & Sync** sends the master's changed project files, the Output Setup files and the live graph state to the render nodes, hash-diffed. What a node can apply without reloading depends on what changed: **assets and setups apply live** — the node keeps running, no restart. **Operator data (.t3/.t3ui) and operator code (.cs/.csproj) require a reload**, and the node restarts into the updated show (reopening the deployed project and rejoining automatically). A deploy that transfers nothing leaves the node up. Repeated restarts are fenced by a flood guard (more than four in two minutes pauses restarts for five minutes).
- Projects this machine received through show-control features (a boygroup deploy, a collab download) are grouped under **Network projects** in the project Hub, tagged `[boygroup]` / `[collab]`.
- Setup edits made on the master are forwarded as they are saved, so slaves follow a venue change live.
- **Live values**: the master keeps publishing the focused composition's input values, so parameter tweaks reach the nodes within a moment as show state - no file deploy and no restart. On a node those values are applied without marking its project modified, so they never look like local edits and never trigger a redeploy.
- **The group follows the master's project**: a node that shows another project (the master switched shows, or the node reopened its own) is switched to the shared one - in place when it is installed, otherwise it receives it and restarts into it.
- The master drives playback and the slaves follow its clock with NTP-style precision: each slave measures the clock offset to the master from three-stamp pings (a quick burst, then a slow refresh; the least-queueing sample wins), and the master sends play-at anchors — the bars it will reach 80 ms ahead, stamped with that instant. The slave converts the instant into its own monotonic clock and switches when it arrives, so network jitter shifts nothing; between anchors it dead-reckons on a monotonic clock and the cursor advances every frame. A late anchor is advanced by its lateness rather than snapping back, and every switch self-measures its correction (shown as the node's sync error), so a node following a wrong clock is visible instead of just busy. Measured on a wired LAN: phase error ≈ 2 ms, paused phase exactly zero.

- **Clock sync** chooses where a slave takes its clock from. *Internal* (the default) is TiXL's own NTP exchange and needs nothing else. *PTP* trusts this machine's OS clock — use it when the venue runs PTP (linuxptp, w32time with a PTP source): every machine is then held on the same reference within microseconds, anchors apply directly, and TiXL skips its own offset measurement. All clocks in the chain are read with the highest resolution the OS offers (100 ns on Windows).
- Every applied anchor is checked against the position the slave's own timer predicted for that instant. The measured correction is shown per node on the master (`sync` in ms) and on the slave, so a node that drifts can be spotted without watching the outputs.
- **Delay −10/+10** on a render node shifts its presentation in time (up to ±500 ms, positive = later). The value is remembered per node and re-sent when it reconnects, which lines up projectors or capture chains that process the signal at different speeds.
- **Play all / Pause all / Stop all** drive the master and the render nodes together. **Stop all** follows the master's timeline settings: with "reset time after playback" enabled the show returns to the bar playback started from, otherwise it stops at the current bar. Playback started from the master's own transport keys is followed by the render nodes just the same.
- Each slave presents the outputs connected in its own **[Output Setup](OutputSetup.md) → Local Bindings**. On a master, the connected nodes' displays are listed in that same column under the node's name: drop an output on one to route it on that machine. The master sends the request, the render node binds it in its own `outputs.machine.json` and reports back, so the edge, the bound state and **Unbind** always reflect the client's real state. Only outputs and setups are shared; `outputs.machine.json` is never overwritten by a deploy.

The master shows the added nodes, their local output bindings and their deployment status, plus per-node diagnostics: round trip, clock offset, measured sync error, time since the last sync, frame rate and version. Heartbeats run at 1 Hz on both sides and a silent peer is dropped after 4 s, so a cable pull or a crashed master is noticed immediately instead of leaving the slave dead-reckoning a show that no longer exists. A slave keeps its role after a master disappears and reconnects on its own: it remembers the master's name and rejoins as soon as that master — or, for a slave that never joined one, the first master that appears — is on the network. Boygrouping therefore always wins over whatever the machine was doing before once it is enabled.

The **Network** section of the Collaboration, Boygrouping and Failover settings is shared: pick the show network's Ethernet adapter there when the machine also has Wi-Fi, VPN or Hyper-V adapters. **Automatic** keeps the previous behavior. A pinned adapter takes effect the next time boygrouping is enabled; failover rebinds immediately.

## Input replication

A render node does not need the controllers the show was built with. The master forwards its hardware input to the nodes that are connected:

- **MIDI** messages are captured once at the device layer and replayed through the same `[MidiInput]` operators on every node, so device, channel, controller, note and control-range filters produce exactly the master's values. MIDI clock and note/CC traffic are both carried.
- **OSC** packets are forwarded as their original bytes and parsed on the node, so multi-argument messages keep every value and type. Port and address filters behave as they do on the master.
- Input traffic is only captured while at least one render node is connected. Events are batched once per frame; an overflowing queue drops events and reports it in the log rather than growing without bound.
- A slave that truly has its own controllers still uses them while the master sends no MIDI/OSC, and switches to the master's input as soon as replicas arrive.

## Show failover

Show failover keeps a redundant pair of machines streaming. Both run the same composition; only the **Active** one sends Art-Net, sACN and streams. The **Standby** renders silently and takes over within `timeout factor × heartbeat` (about 1.5 s with the defaults) when the active machine stops sending heartbeats.

- Heartbeats are multicast on every real network interface (or only on the adapter selected under **Network**), so a pair can be rehearsed on a single computer.
- **Pin the show network when the machine has more than one interface.** Unpinned heartbeats follow the OS's default adapter, which may be Wi-Fi, a VPN or a Hyper-V adapter; the two machines then cannot hear each other and **both become Active — both stream the show**. The log warns about this when failover starts unpinned on a multi-adapter machine, and taking the wrong adapter is fixed immediately by selecting the show network under **Network**.
- The higher configured priority wins. Preemption, where a returning higher-priority machine takes the role back, is optional.
- A role change includes a short mutual mute so both machines never stream at the same time.
- `[ArtnetOutput]` and `[SacnOutput]` report `Standby` in their status while they are muted.
- **The standby follows the active node's project**: the heartbeat carries the project name, and the standby opens it when it is installed locally - so a takeover renders the same show instead of whatever the standby happened to have open. Both machines are expected to have the show installed; the heartbeat carries only the name.

Select the Failover mode in the menu bar to arm it, then configure show id (machines with the same id form a pair), priority, heartbeat interval, timeout factor, preemption and network interface.
