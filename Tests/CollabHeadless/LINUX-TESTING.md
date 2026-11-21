# Testing the collab / boygroup / failover layer on Linux (AI runbook)

This document is written for an AI agent operating a Linux machine. Everything below is
self-contained: clone, build, run, report. Do not push commits unless explicitly asked.

## Context

Branch `feat/collabmulti` (fork: `https://github.com/banidu-512/tixl`) adds three network
features to TiXL. The full TiXL editor targets `net10.0-windows` and will NOT build or run
on Linux — that is expected and not a problem. What runs everywhere is:

1. **`Tests/CollabHeadless`** — a `net10.0` console harness that compile-includes the real
   production sources (framing, auth, presence packets, file deployment, failover arbiter)
   and tests them on real sockets and real processes. Building it is also the POSIX
   compile gate: it fails if any network-layer file uses a Windows-only API.
2. **`CollabRelay`** — the standalone room relay, retargeted to plain `net10.0`.

## Setup

```bash
git clone https://github.com/banidu-512/tixl.git tixl-test
cd tixl-test
git checkout feat/collabmulti
```

Requires the .NET 10 SDK (`global.json` pins `10.0.0` with `latestMinor` roll-forward):

```bash
dotnet --version   # must be 10.0.x
# if missing:  https://dotnet.microsoft.com/download/dotnet/10.0
# (Debian/Ubuntu: follow the dotnet install script; nothing else is needed)
```

Only the two projects above are built — do NOT try to build the solution or the editor.

## Run

```bash
# 1. Headless suite (9 tests; spawns child processes; uses multicast loopback)
dotnet run --project Tests/CollabHeadless/CollabHeadless.csproj -c Debug

# 2. Relay smoke test (needs python3, stdlib only)
dotnet build CollabRelay/CollabRelay.csproj -c Debug
dotnet run --project CollabRelay/CollabRelay.csproj -c Debug --no-build -- 44710 &
sleep 2
python3 Tests/CollabHeadless/relay_smoke.py 44710
kill %1
```

## Expected output

Test 1 must print exactly:

```
PASS framing.roundtrip
PASS framing.maxsize
PASS json.decode
PASS presence.roundtrip
PASS auth.mac
PASS deploy.inventory
PASS deploy.transfer
PASS deploy.pathsafety
      failover: takeover after NNN ms (timeout budget 450 ms)
PASS failover.election
ALL TESTS PASSED
```

(Reference run on Windows: takeover measured 495–557 ms. Anything under ~2000 ms is fine;
what matters is PASS on every line.)

Test 2 must print `RELAY SMOKE TEST PASSED` with all 9 PASS lines.

## What each test covers

- `framing.*` — the length-prefixed binary frame protocol every collab/boygroup message
  rides on, including the 4 MiB cap and empty/100 KB payloads over real TCP.
- `json.decode` — JSON message discriminator rules (relay control lines start with `{`,
  frames must too).
- `presence.roundtrip` — the binary UDP presence packet (cursor position + selection GUIDs).
- `auth.mac` — PBKDF2 key derivation + HMAC challenge/response, wrong-password rejection.
- `deploy.*` — boygroup file deployment: inventory diff (size+mtime, smallest-first),
  192 KB chunked transfer with SHA-256 verify, atomic temp+move, **mtime restoration**
  (the cross-platform bug this guards: without it Linux clients would re-receive every
  file forever), and path-escape rejection.
- `failover.election` — two real processes elect via multicast (224.0.0.157:44910):
  master ACTIVE / backup Standby → kill master → backup promotes → restarted master
  preempts back. This is the show-critical Artnet failover logic.

## If something fails

Capture and report ALL of:

```bash
uname -a
dotnet --info | head -20
ip -brief addr          # multicast needs at least one interface with a route
dotnet run --project Tests/CollabHeadless/CollabHeadless.csproj -c Debug   # full output
```

Known Linux-specific risk: if `failover.election` alone fails, check the firewall allows
outbound multicast (224.0.0.0/4) and `ip route` has a default route — the arbiter joins
the group on the default interface. Everything else uses plain loopback/unicast.

Do not modify source code to make a test pass; report the failure verbatim instead.
