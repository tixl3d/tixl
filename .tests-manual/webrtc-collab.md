---
id: webrtc-collab
title: Peer-to-Peer Online Collaboration
scope: collaboration
tags: [network, online, video]
added: 2026-11-01
added-in-version: 4.4
prerequisites:
  - Two machines (or two editor instances) on networks that can reach a common CollabRelay.
  - The Video operator package is available (it ships with the editor).
  - A rendered output is pinned so the host has a preview texture.
related-help:
  - ../.help/docs/using/Collaboration.md
---

Covers the direct WebRTC path of an online session: the handshake still runs over the relay, the
session switches to a peer-to-peer data channel, the host's preview arrives as an H.264 video
track, and a relay-only participant keeps working when a direct path is impossible. Reference
image for the last step: `webrtc-collab.png` in this folder.

## Step: Host an online session

**Action:**
Open **Settings → Collaboration**, press **Start Session** and copy the session ID.

**Expected:**
- The status reads `Hosting via relay …, room <ID>`.
- **Participants** shows only the host.

## Step: Join from the second machine

**Action:**
On the other machine, paste the invite with **Paste Invite and Join**.

**Expected:**
- The client reaches `Connected as …`.
- Both sides list each other in **Participants**.

## Step: Verify the direct path

**Action:**
Wait a few seconds and watch the participant row.

**Expected:**
- The participant entry changes from `P2P...` to `P2P` within a few seconds.
- **Leave Session** still works and the chat still delivers messages.

## Step: Verify the host preview

**Action:**
Pin a visible operator output on the host (for example `CheckerBoard`), open the **Collab**
window on the client and keep the session running.

**Expected:**
- The client's **Collab** window shows a **Host Preview** image of the host's output.
- The image updates when the host changes the graph.
- The host's window shows `Preview: <encoder>` (for example `NVIDIA NVENC` or `OpenH264`).

## Step: Relay fallback

**Action:**
Block the direct path between the machines (for example block inbound UDP on the host firewall),
then have the client leave and rejoin the same room.

**Expected:**
- The session still connects and the graph still synchronizes.
- The participant row shows no `P2P` label; the preview is simply absent.
- Unblocking the path and rejoining restores `P2P` and the preview.

## Step: Preview opt-out

**Action:**
On the host, uncheck **Advanced → Peer-to-Peer → Share preview with participants**, then have the
client leave and rejoin.

**Expected:**
- The client's **Collab** window has no **Host Preview** section.
- Data synchronization and `P2P` remain unaffected.
