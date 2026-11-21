#!/usr/bin/env python3
"""Builds and exercises the all-operators mediapipe test project.

Opens _mediapipeTest, makes sure all seven mediapipe operators exist and are fed from the
camera, then verifies each one: pin its DebugTexture, pump frames, read its count output,
screenshot the output window, and collect any error-log lines mentioning it.

Usage: python3 Scripts/mediapipe_all_test.py [port] [screenshot-dir]
"""
import json
import os
import socket
import sys
import time

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 9042
SHOT_DIR = sys.argv[2] if len(sys.argv) > 2 else "/tmp/tixl-mediapipe/all"
os.makedirs(SHOT_DIR, exist_ok=True)

# symbol name -> (count output or None, enable-input name)
OPS = {
    "FaceDetection":         ("FaceCount", None),
    "FaceLandmarkDetection": ("FaceCount", None),
    "GestureRecognition":    ("HandCount", None),
    "HandLandmarkDetection": ("HandCount", None),
    "ImageSegmentation":     (None,        None),
    "ObjectDetection":       ("ObjectCount", None),
    "PoseLandmarkDetection": ("PoseCount", None),
}

s = socket.create_connection(("127.0.0.1", PORT), timeout=30)
s.settimeout(180)
f = s.makefile("rw", encoding="utf-8", newline="\n")
_counter = 0


def call(method, **args):
    global _counter
    _counter += 1
    f.write(json.dumps({"id": str(_counter), "method": method, **args}) + "\n")
    f.flush()
    r = json.loads(f.readline())
    if not r.get("ok"):
        raise RuntimeError(f"{method} failed: {r.get('error')}")
    return r.get("result") or {}


def try_call(method, **args):
    global _counter
    _counter += 1
    f.write(json.dumps({"id": str(_counter), "method": method, **args}) + "\n")
    f.flush()
    return json.loads(f.readline())


log_seq = call("getLogTail", maxCount=1)["latestSeq"]

# --- project -----------------------------------------------------------------
response = try_call("openProject", name="_mediapipeTest")
if response.get("ok"):
    print("opened _mediapipeTest")
else:
    print("created:", call("newProject", name="_mediapipeTest"))
    call("openProject", name="_mediapipeTest")
call("pumpFrames", count=10)

# --- camera ------------------------------------------------------------------
video = None
children = {}
for c in call("getGraphState")["children"]:
    children[c["symbolName"]] = c["childId"]
    if c["symbolName"] == "VideoDeviceInput":
        video = c["childId"]

if video is None:
    video = call("addOp", symbolName="VideoDeviceInput", posX=-400, posY=0)["childId"]
    print("added VideoDeviceInput")
call("setInput", childId=video, inputName="Active", value=True)

connections = {(c["sourceParentOrChildId"], c["targetParentOrChildId"])
               for c in call("getGraphState")["connections"]}

# --- ops ---------------------------------------------------------------------
ids = {}
x = 0
for name in OPS:
    if name in children:
        ids[name] = children[name]
        print(f"{name}: already present")
        continue
    ids[name] = call("addOp", symbolName=name, posX=x, posY=-700)["childId"]
    x += 320
    print(f"{name}: added")

for name, child in ids.items():
    if name != "VideoDeviceInput" and (video, child) not in connections:
        call("connect", sourceChildId=video, sourceOutput="Texture",
             targetChildId=child, targetInput="InputTexture")
    if name != "VideoDeviceInput":
        call("setInput", childId=child, inputName="Debug", value=True)
        call("setInput", childId=child, inputName="Acceleration", value="Xnnpack")
print("all ops wired to the camera")

# --- verify each op ----------------------------------------------------------
results = {}
for name, (count_output, _) in OPS.items():
    child = ids[name]
    call("pin", childId=child, outputName="DebugTexture")
    call("pumpFrames", count=90)  # give the worker time to init + process

    count = None
    if count_output:
        # detections fluctuate with what's in view - poll briefly and keep the best
        deadline = time.time() + 5
        while time.time() < deadline:
            call("pumpFrames", count=30)
            value = call("getOutput", childId=child, outputName=count_output)["value"]
            count = value if count is None else max(count or 0, value or 0)
            if count and count > 0:
                break

    shot = os.path.join(SHOT_DIR, f"{name}.png")
    call("screenshot", path=shot, target="output")

    results[name] = count
    label = f"{count} detected" if count_output else "no count output - see screenshot"
    print(f"{name}: {label}  [{shot}, {os.path.getsize(shot)} bytes]")

# --- errors since the run started --------------------------------------------
entries = call("getLogTail", sinceSeq=log_seq, minLevel="warning", maxCount=300)["entries"]
op_errors = [e for e in entries
             if any(k in (e.get("message") or "") for k in OPS) and "not found" not in (e.get("message") or "")]
print(f"\nerror/warning log lines mentioning the ops: {len(op_errors)}")
for e in op_errors[:12]:
    print("  ", e.get("level"), "|", (e.get("message") or "")[:150])

call("save")
print("project saved")
print("DONE")
