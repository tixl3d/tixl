# Live collaboration

Several TiXL editors can work on the same graph at the same time. Everyone sees the other participants' cursors, selections and "editing" badges, and every graph edit is replayed on all machines.

Sessions are configured in **Settings → Collaboration**. The feature is experimental. Collaboration is one of three mutually exclusive show-control modes (Boygroup / Failover / Collab) selected in the menu bar — starting a session from this panel claims the Collab mode and ends the others on this machine.

## The Collab window

The Collaboration window is organized into five one-line tabs:

- **Chat** — the session chat. It needs a running session; host or join one first.
- **Party matching** — the session browser with the live sessions you can join, and hosting your own.
- **Party management** — the participants of the current session, pending join requests, whispers and the party video feed.
- **Remote** — pair with and manage another TiXL installation.
- **Boygroup** — the Boygroup settings; see [Boygrouping and show failover](BoygroupingAndFailover.md).

## Hosting and joining

A session runs on the party backend or on a relay. Backend sessions are listed live in the session browser, and the participants connect peer-to-peer over WebRTC: the server only brokers who meets whom, the session's traffic — including video — runs directly between the editors.

1. **Log In with Discord** (optional but recommended). TiXL opens discord.com in your browser for the OAuth2 login; the browser returns to TiXL automatically. Your display name and avatar come from Discord. The account must be a member of the TiXL Discord server — the login checks membership and points you at the invite if not. Without a Discord login a local nickname is used.
2. **Start Session** on **Party matching** opens a room and shows the **session ID**. With a **Session title** and **List in session browser** switched on, the session appears for everyone under **Open sessions** on the same backend or relay. Without listing, others join with the session ID, with **Copy Invite** / **Paste Invite and Join** (which carry the server address and the ID together), or by direct address (see below).
3. Joining a listed session opens a **join ticket**: the host sees the request and approves or rejects it. Before joining, a visitor can **whisper** the host — a short question from the session browser that the host answers on the Party management tab, so the answer reaches the visitor before they join.
4. Once approved, the connection is negotiated and the editor joins.

On join a client downloads the host's project if it doesn't have it, restarts into it and rejoins automatically. If the host drops out, the session is handed over and another participant opens it again as the new host.

## The party video feed

On the Party management tab a participant can share a video feed with the party, for example a real webcam feed. It is delivered over the session's peer connections; when the sharing machine disconnects, the feed stops.

## Remote management

Two installations can be paired so one manages the other: press **Pair this computer** under **Settings → Collaboration**, or **Add remote TiXL** on the Remote tab and enter the one-time code it shows. The code expires — press again for a new one. Managing a remote computer needs a Discord login.

A paired computer can be connected to and disconnected from a session remotely, and it reports its status back, so a machine in the next room or on site can be run from your editor. **Allow remote sessions on this computer** decides whether remote management may do that here, and **Forget pairing** ends the pairing. A remote computer that joins the session of the installation it is paired with is approved automatically, without a join ticket.

## LAN and direct sessions

- **LAN**: **Advanced → Host on LAN** hosts on a configurable TCP port (44700 by default); the others find it through LAN discovery or join with a direct address under **Advanced → Join by address**.
- **Projects window**: running LAN sessions are listed next to the projects, and a project's context menu offers **Host Collaboration Session**.
- **Password**: a session can be password protected. Authentication is a PBKDF2/HMAC challenge-response; the password itself never crosses the network.

## Identity

Discord login uses the OAuth2 browser flow (PKCE, a public Discord application, no client secret involved). The identity persists in this machine's user settings until you log out. A local nickname is the fallback.

## What is synchronized

- Adding and deleting operators, connections, parameter changes, renames and node moves.
- **Undo and redo are session-wide while connected**: Ctrl+Z reverts the last edit of any participant. The host keeps the shared history.
- **Late joiners** receive a snapshot of every composition the session has touched, replacing their local state in those compositions.
- **Soft parameter locks**: a parameter someone is editing is shown disabled for everyone else until it is released.

The **Collaboration** window shows the participants and a session chat. Each participant row shows the round trip to that machine; everyone can pick their own cursor color, which other participants see within a moment. The host can remove a participant ("Remove" + confirmation): the removed editor leaves the session and lands in the project hub, and may rejoin unless the session has a password.

## Platforms

Collaboration uses plain TCP/UDP sockets and runs on Windows, Linux and macOS. Paths are compared case-sensitively on Linux and macOS and case-insensitively on Windows.
