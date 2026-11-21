"""
Protocol test for TiXLCollabRelay. Start a relay, then run this against it:

    dotnet run --project CollabRelay -- 45990
    python CollabRelay/protocol-test.py 45990

Covers the short replies old editors expect, the session list, identity fields, room keys
(a silent host can be replaced, a live one cannot, the same host instance reclaims its room),
and members being closed when their host leaves. Takes about 30 s: one case waits for a host
to count as silent. Discord verification needs a real token and is only covered by the
"bad token stays unverified" case.
"""
import socket, json, struct, time, sys
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 45990
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
def closed(s, wait=3):
    s.settimeout(wait)
    try:
        return s.recv(1) == b""
    except socket.timeout:
        return False
    except OSError:
        return True
def keepalive(s):
    payload = b"\x00\x03" + b"\x00" * 8   # slot 0, heartbeat type
    s.sendall(struct.pack("<i", len(payload)) + payload)
ok = True
def check(name, cond, detail=""):
    global ok
    ok &= bool(cond)
    print(("PASS " if cond else "FAIL ") + name, detail)

# 1. old-style host/join still work with the short replies
h = conn({"role": "host", "room": ""}); r = line(h)
check("legacy host reply", r.get("ok") and "you" not in r, r)
m = conn({"role": "join", "room": r["room"]}); jr = line(m)
check("legacy join reply", jr.get("ok") and "host" not in jr, jr)
ann = line(h)
check("legacy member line is short", ann == {"t": "member", "slot": jr["slot"]}, ann)
h.close()
check("members closed when host leaves", closed(m))

# 2. extended host: listed room with metadata, unverified identity
h = conn({"role": "host", "room": "", "v": 2, "key": "k1", "name": "Márton", "lang": "hu", "title": "Techno jam ♫", "purpose": "Jam", "public": True, "pwd": True, "project": "Demo"})
r = line(h); code = r["room"]
check("extended host reply", r["ok"] and r["you"]["name"] == "Márton" and r["you"]["verified"] is False, r)
l = conn({"role": "list"}); rooms = line(l)["rooms"]
mine = [x for x in rooms if x["code"] == code]
check("room listed", len(mine) == 1 and mine[0]["title"] == "Techno jam ♫" and mine[0]["host"] == "Márton" and mine[0]["members"] == 1 and mine[0]["pwd"] is True, mine)
hp = conn({"role": "host", "room": "", "v": 2, "key": "k9"}); rp = line(hp)
rooms = line(conn({"role": "list"}))["rooms"]
check("private room not listed", all(x["code"] != rp["room"] for x in rooms))

# 3. extended join: member told who the host is, host told who joined
m = conn({"role": "join", "room": code, "v": 2, "name": "Guest", "lang": "en-US"}); jr = line(m)
check("join reply has host", jr["ok"] and jr["host"]["name"] == "Márton" and jr["you"]["name"] == "Guest", jr)
ann = line(h)
check("member line has identity", ann["t"] == "member" and ann["name"] == "Guest" and ann["lang"] == "en-US" and ann["verified"] is False, ann)
rooms = line(conn({"role": "list"}))["rooms"]
check("member count", [x for x in rooms if x["code"] == code][0]["members"] == 2)

# 4. room key: live host cannot be replaced; wrong key gets another code
keepalive(h)
t = conn({"role": "host", "room": code, "v": 2, "key": "k1"}); tr = line(t)
check("takeover refused while host alive", tr.get("ok") is False and tr.get("error") == "room has a live host", tr)
t2 = conn({"role": "host", "room": code, "v": 2, "key": "wrong"}); tr2 = line(t2)
check("wrong key gets a different code", tr2.get("ok") and tr2["room"] != code, tr2)

# 5. silent host is replaced, old host and members are cut loose
print("waiting for the host to count as silent...")
time.sleep(21)
t3 = conn({"role": "host", "room": code, "v": 2, "key": "k1", "name": "Successor", "public": True}); tr3 = line(t3)
check("takeover of silent host", tr3.get("ok") and tr3["room"] == code, tr3)
check("old host closed", closed(h))
check("old member closed", closed(m))
m2 = conn({"role": "join", "room": code, "v": 2, "name": "Guest"}); jr2 = line(m2)
check("member finds room under new host", jr2.get("ok") and jr2["host"]["name"] == "Successor", jr2)

# 6. bad token falls back to unverified (Discord unreachable or rejects it)
b = conn({"role": "host", "room": "", "v": 2, "token": "not-a-token", "name": "X"}); br = line(b)
check("bad token is unverified", br.get("ok") and br["you"]["verified"] is False, br)
check("malformed request", line(conn({"nope": 1})).get("ok") is False)

# 7. host with key+instance; its own dead connection still looks alive to the relay
h = conn({"role": "host", "room": "", "v": 2, "key": "kk", "inst": "A"}); r = line(h); code = r["room"]
m = conn({"role": "join", "room": code, "v": 2, "name": "G"}); line(m); line(h)
keepalive(h)
other = conn({"role": "host", "room": code, "v": 2, "key": "kk", "inst": "B"}); o = line(other)
check("participant cannot take a live host's room", o.get("error") == "room has a live host", o)
back = conn({"role": "host", "room": code, "v": 2, "key": "kk", "inst": "A"}); b = line(back)
check("same host instance reclaims its room at once", b.get("ok") and b["room"] == code, b)
check("its dead connection is closed", closed(h))
check("member of dead connection is cut loose", closed(m))
late = conn({"role": "host", "room": code, "v": 2, "key": "kk", "inst": "B"}); l = line(late)
check("successor then finds a live host", l.get("error") == "room has a live host", l)
# long non-ASCII title survives and the list line stays ASCII
t = conn({"role": "host", "room": "", "v": 2, "title": "Ünnepi fény-jam 🎆 " * 5, "public": True, "name": "Ж" * 60}); tr = line(t)
s = socket.create_connection(("127.0.0.1", PORT)); s.sendall(b'{"role":"list","room":"","v":2}\n')
raw = b""
while not raw.endswith(b"\n"): raw += s.recv(65536)
rooms = json.loads(raw)["rooms"]
mine = [x for x in rooms if x["code"] == tr["room"]][0]
check("list line is pure ASCII", all(c < 128 for c in raw))
check("title and name are cut to length", len(mine["title"]) <= 64 and len(mine["host"]) <= 48, mine)
# 8. live listing update (Change entry / Delete entry): key-authenticated
u = conn({"role": "host", "room": "", "v": 2, "key": "kU", "title": "Old title", "purpose": "Jam", "public": True}); ur = line(u); ucode = ur["room"]
check("listed after host", any(x["code"] == ucode for x in line(conn({"role": "list"}))["rooms"]))
bad = conn({"role": "update", "room": ucode, "key": "WRONG", "title": "Hacked"}); badr = line(bad)
check("update with wrong key refused", badr.get("ok") is False, badr)
up = conn({"role": "update", "room": ucode, "key": "kU", "title": "New title", "purpose": "Review", "public": False}); upr = line(up)
check("unlist accepted", upr.get("ok") is True, upr)
check("unlisted room gone from list", not any(x["code"] == ucode for x in line(conn({"role": "list"}))["rooms"]))
relist = conn({"role": "update", "room": ucode, "key": "kU", "title": "New title", "purpose": "Review", "public": True}); relr = line(relist)
check("change entry accepted", relr.get("ok") is True, relr)
mine = [x for x in line(conn({"role": "list"}))["rooms"] if x["code"] == ucode]
check("list shows new title", len(mine) == 1 and mine[0]["title"] == "New title" and mine[0]["purpose"] == "Review", mine)

# 9. whispers: guest line reaches the host link, host reply reaches the listening guest
h = conn({"role": "host", "room": "", "v": 2, "key": "kW", "name": "HostW"}); wr = line(h); wcode = wr["room"]
w = conn({"role": "whisper", "room": wcode, "name": "Visitor", "text": "can I join?", "listen": True}); wl = line(w)
check("whisper sent", wl.get("ok") is True, wl)
hostline = line(h)
check("host receives whisper", hostline.get("t") == "whisper" and hostline["from"] == "Visitor" and hostline["text"] == "can I join?", hostline)
rep = conn({"role": "whisper", "room": wcode, "key": "kW", "to": "Visitor", "text": "yes in 5 min"}); got = line(rep)
check("host reply delivered", got.get("ok") is True and got.get("delivered") == 1, got)
guestline = line(w)
check("listener gets reply", guestline.get("t") == "whisper" and guestline["from"] == "HostW", guestline)
ghost = conn({"role": "whisper", "room": "NOPE01", "name": "V", "text": "hi"}); g = line(ghost)
check("whisper to unknown room", g.get("ok") is False, g)
h.close(); w.close()

print("ALL PASS" if ok else "SOME FAILED")
sys.exit(0 if ok else 1)
