#!/usr/bin/env python3
"""Smoke test for the TiXL collab relay (CollabRelay/). Zero dependencies, stdlib only.

Verifies the wire contract the editor depends on:
  1. host/join control-line handshake and the member announcement back to the host
  2. member -> host frames arrive slot-tagged: [len4][slot1][type1][payload]
  3. host -> member targeted sends arrive stripped: [len4][type1][payload]
  4. host -> 0xFF broadcast reaches every member, stripped
  5. room-not-found is rejected with ok:false

Usage: python3 relay_smoke.py [port]   (default 44710)
"""
import json
import socket
import struct
import sys
import threading

HOST = "127.0.0.1"


def recv_exact(sock, n):
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("socket closed while reading")
        buf += chunk
    return buf


def recv_line(sock):
    buf = b""
    while not buf.endswith(b"\n"):
        chunk = sock.recv(1)
        if not chunk:
            raise ConnectionError("socket closed while reading a line")
        buf += chunk
    return buf.decode().strip()


def send_frame(sock, frame_type, payload):
    sock.sendall(struct.pack("<iB", len(payload) + 1, frame_type) + payload)


def send_host_frame(sock, slot, frame_type, payload):
    # Host -> relay: [len(incl. slot)][slot][type][payload]. slot 0xFF broadcasts.
    sock.sendall(struct.pack("<iBB", len(payload) + 2, slot, frame_type) + payload)


def recv_frame(sock, tagged=False):
    """Untagged (member side): [len][type][payload]. Tagged (host side): [len][slot][type][payload]."""
    total, = struct.unpack("<i", recv_exact(sock, 4))
    rest = recv_exact(sock, total)
    if tagged:
        return rest[0], rest[1], rest[2:]
    return rest[0], rest[1:]


def connect_role(port, request_line, timeout=5.0):
    sock = socket.create_connection((HOST, port), timeout=timeout)
    sock.sendall(request_line.encode() + b"\n")
    reply = json.loads(recv_line(sock))
    return sock, reply


def main():
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 44710

    failures = []

    def check(name, ok, detail=""):
        print(("PASS " if ok else "FAIL ") + name + (f" ({detail})" if detail and not ok else ""))
        if not ok:
            failures.append(name)

    # 1. Host registers a room.
    host, host_reply = connect_role(port, json.dumps({"role": "host", "room": "SMOKE1"}))
    check("host registers room", host_reply.get("ok") is True and host_reply.get("room") == "SMOKE1", str(host_reply))

    # 2. Two members join; host must see one member announcement per join.
    m1, m1_reply = connect_role(port, json.dumps({"role": "join", "room": "SMOKE1"}))
    check("member1 joins", m1_reply.get("ok") is True and m1_reply.get("slot") == 1, str(m1_reply))
    announcement = json.loads(recv_line(host))
    check("host told about member1", announcement == {"t": "member", "slot": 1}, str(announcement))

    m2, m2_reply = connect_role(port, json.dumps({"role": "join", "room": "SMOKE1"}))
    check("member2 joins at slot 2", m2_reply.get("slot") == 2, str(m2_reply))
    announcement = json.loads(recv_line(host))
    check("host told about member2", announcement == {"t": "member", "slot": 2}, str(announcement))

    # 3. member1 -> host arrives slot-tagged: [slot][type][payload].
    send_frame(m1, 1, b"hello-from-m1")
    slot, frame_type, payload = recv_frame(host, tagged=True)
    check("member->host tagged", slot == 1 and frame_type == 1 and payload == b"hello-from-m1",
          f"slot={slot} type={frame_type} payload={payload!r}")

    # 4. host -> member2 targeted send arrives stripped.
    send_host_frame(host, 2, 1, b"only-for-m2")
    frame_type, payload = recv_frame(m2)
    check("host->member2 targeted", frame_type == 1 and payload == b"only-for-m2", repr(payload))

    # 5. host broadcast (slot 0xFF) reaches both members.
    got = []

    def reader(sock, out):
        frame_type, payload = recv_frame(sock)
        out.append(payload)

    t1 = threading.Thread(target=reader, args=(m1, got))
    t2 = threading.Thread(target=reader, args=(m2, got))
    t1.start()
    t2.start()
    send_host_frame(host, 0xFF, 1, b"broadcast")  # TCP buffers it; readers drain whenever they run
    t1.join(timeout=5)
    t2.join(timeout=5)
    check("broadcast reaches both", got == [b"broadcast", b"broadcast"], repr(got))

    # 6. Unknown room rejected.
    _, bad_reply = connect_role(port, json.dumps({"role": "join", "room": "NOPE"}))
    check("unknown room rejected", bad_reply.get("ok") is False, str(bad_reply))

    for sock in (host, m1, m2):
        sock.close()

    print("RELAY SMOKE TEST " + ("PASSED" if not failures else "FAILED: " + ", ".join(failures)))
    return 0 if not failures else 1


if __name__ == "__main__":
    sys.exit(main())
