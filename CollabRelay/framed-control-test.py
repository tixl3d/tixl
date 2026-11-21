"""
Framed control test for TiXLCollabRelay. Start a relay, then run this against it:

    dotnet run --project CollabRelay -- 45991
    python CollabRelay/framed-control-test.py 45991

A host that asks for "ctl":"frame" gets member/left messages as frames on slot 0xFE, so frames
whose little-endian length starts with 0x7B ('{') are not mistaken for control lines.
"""
import socket, json, struct, sys, time
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 45991
CONTROL_SLOT = 0xFE
ok = True

def check(name, cond, detail=""):
    global ok
    ok &= bool(cond)
    print(("PASS " if cond else "FAIL ") + name, detail)

def conn(req):
    s = socket.create_connection(("127.0.0.1", PORT), timeout=5)
    s.sendall((json.dumps(req) + "\n").encode())
    return s

def line(s):
    b = b""
    while not b.endswith(b"\n"):
        c = s.recv(1)
        if not c: return None
        b += c
    return json.loads(b)

def recv_exact(s, n):
    b = b""
    while len(b) < n:
        c = s.recv(n - len(b))
        if not c: raise EOFError
        b += c
    return b

def read_frame(s):
    length = struct.unpack("<i", recv_exact(s, 4))[0]
    return length, recv_exact(s, length)

h = conn({"role": "host", "room": "", "v": 2, "ctl": "frame", "key": "kf"})
r = line(h)
check("framed host reply echoes ctl", r.get("ok") and r.get("ctl") == "frame", r)

m = conn({"role": "join", "room": r["room"], "v": 2, "name": "Guest"})
jr = line(m)
check("join ok", jr.get("ok"), jr)

length, frame = read_frame(h)
body = json.loads(frame[2:])
check("member arrives as control frame", frame[0] == CONTROL_SLOT and frame[1] == 0 and body["t"] == "member" and body["slot"] == jr["slot"], body)

# member -> host frame whose host-side length is 379 (low byte 0x7B)
payload = bytes([1]) + b"x" * 377
m.sendall(struct.pack("<i", len(payload)) + payload)
length, frame = read_frame(h)
check("0x7B-length frame from member reaches host intact", length == 379 and frame[0] == jr["slot"] and frame[1:] == payload, (length, frame[:4]))

# host -> member frame of total length 379
slot = jr["slot"]
hp = bytes([slot, 1]) + b"y" * 377
h.sendall(struct.pack("<i", len(hp)) + hp)
length, frame = read_frame(m)
check("0x7B-length frame from host reaches member intact", length == 378 and frame == hp[1:], length)

# host keeps working afterwards (no desync): keepalive then member leaves
h.sendall(struct.pack("<i", 10) + b"\x00\x03" + b"\x00" * 8)
m.close()
length, frame = read_frame(h)
body = json.loads(frame[2:])
check("left arrives as control frame after the 0x7B frames", frame[0] == CONTROL_SLOT and body["t"] == "left" and body["slot"] == slot, body)

# a host that does not ask for framed control still gets lines
h2 = conn({"role": "host", "room": "", "v": 2})
r2 = line(h2)
check("legacy extended host gets no ctl", r2.get("ok") and "ctl" not in r2, r2)
m2 = conn({"role": "join", "room": r2["room"], "v": 2, "name": "G2"})
line(m2)
ann = line(h2)
check("legacy extended host still gets a line", ann["t"] == "member", ann)

print("ALL PASS" if ok else "SOME FAILED")
sys.exit(0 if ok else 1)
