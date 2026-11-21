# TiXL (Tooll3) - A realtime animation toolkit

[![Discord](https://img.shields.io/discord/823853172619083816.svg?style=for-the-badge)](https://discord.gg/YmSyQdeH3S)
[![MIT License](https://img.shields.io/badge/license-MIT-blue.svg?style=for-the-badge)](https://github.com/tixl3d/tixl/blob/main/LICENSE.txt)
[![Download](https://img.shields.io/badge/Download_TiXL_v4.3.0.4-FF3A9E?style=for-the-badge)](https://github.com/tixl3d/tixl/releases/download/v4.3.0.4/Tixl-v4.3.0.4.exe)
[![tooll-screenshot](https://user-images.githubusercontent.com/1732545/173256422-a4ef9894-d954-4bc3-8c24-000bfbe1c3ad.png)](https://www.youtube.com/watch?v=PrxhwOC9hLw "Tooll3 - A quick overview")

## Overview
TiXL is an open source software to create realtime motion graphics. We are targeting the sweet spot between real-time rendering, graph-based procedural content generation and linear keyframe animation and editing. This combination allows… 
- artists to build audio reactive vj content
- use advanced interfaces for exploring parameters
- or to combine keyframe animation with automation

Technical artists can also dive deeper and use TiXL for advanced development of fragment or compute shaders or to add input from midi controllers and sensors or sources like OSC or Spout. 

We strongly believe in usability and intuitive and beautiful interface design. That's why we experiment with different approaches before striking the right
balance between usability and powerful flexibility. Currently TiXL version 4 is an ongoing development. It's stable enough to produce high-end visuals
create motion graphics use many industry standard features like color correction, scopes and tone mapping, and export small standalone executables.

We hope to bring together a community of artists, developers and creative tinkerers to drive the design and development of the software as a free open source project.

## Live collaboration, Boygrouping & Show Failover (experimental)

**Live collaboration** (Settings → Collaboration, or "Host Collaboration Session" on a project in the Hub) puts several editors on the same graph: LAN discovery or an online relay, password-protected sessions, live cursors, chat, shared undo, parameter locks and project download on join. Clients connect with the TiXL editor or the relay server in `CollabRelay/`.

**Boygrouping** (Settings → Boygrouping) distributes a show to render nodes, vvvv-style: one TiXL instance is the server, others join as clients (LAN discovery or direct address, multiple named groups per server). **Deploy & Sync** pushes changed project files (hash-diffed, chunked, verified), the Output Setup, and the live graph state to every client, which restarts and rejoins automatically. The server drives playback; clients follow the master clock. In the **Output Setup**, each output gets a "Render on machine" assignment so you configure per node what it renders — the client's own display/stream bindings stay local (`outputs.machine.json` is never deployed). The master also replicates its MIDI and OSC input to the render nodes, so the show stays controllable from one machine.

**Show Failover** (Settings → Failover) keeps a redundant pair streaming: two machines run the same composition, the ACTIVE one sends Artnet/sACN/streams, the Standby renders silently and takes over automatically within `timeout × heartbeat` (default ~1.5 s) when the master dies. Heartbeats are multicast (master + backup can be rehearsed on one PC); higher configured priority wins, preemption on master return is optional, and role changes include a brief mutual mute so both sides never stream at once.

**Startup & recovery** (Settings → Startup and Recovery) is for show machines that must come up on their own: open a startup project instead of the Hub, install a per-user login entry (Windows Run key, Linux autostart, macOS LaunchAgent) and run the editor under a crash-restart supervisor.

## Installation
Please follow the [installation guide](https://github.com/tixl3d/tixl/wiki/help.Installation).

## Collaboration (experimental)
This branch adds live multi-user editing of a shared graph. Sessions are configured in **Settings → Collaboration**:

- **LAN**: one instance hosts on a configurable port; others join via network discovery or a direct IP. Optionally password protected (PBKDF2/HMAC challenge-response; the password never crosses the wire).
- **Primary flow**: **Start Session** registers a relay room and shows the **session ID**; joiners enter it (**Join Session**, or **Paste Invite and Join** with a copied invite that carries the relay address). No port forwarding needed. The relay is the included `CollabRelay` server project (`dotnet run --project CollabRelay`).
- **Identity**: a Discord login (the OAuth2 browser popup, PKCE — no client secret involved; the account must be a member of the TiXL Discord server), or a local nickname as fallback.

Synced edits: adding/deleting operators, connections, parameter value changes, renames and node moves. Participants see each other's cursors, selection outlines and "editing" lock badges in the graph. Parameters someone is editing are soft-locked and appear disabled for everyone else until released. **Undo/Redo are session-wide while connected**: Ctrl+Z reverts the last edit of any participant — the host keeps the shared history, and late joiners receive the replay including undo operations. On join, clients receive full snapshots of every composition the session has touched (their local state in those compositions is replaced by the host's), so a matching project is still required but manual re-editing of missed changes is not.

## Get Help
To get started read the [documentation](https://github.com/tixl3d/tixl/wiki) or watch [tutorial videos](https://www.youtube.com/watch?v=eH2E02U6P5Q&list=PLj-rnPROvbn3LigXGRSDvmLtgTwmNHcQs&index=4).

If you have questions or feedback, please join us on Discord: https://discord.gg/YmSyQdeH3S 



