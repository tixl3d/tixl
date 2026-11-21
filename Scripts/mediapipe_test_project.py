#!/usr/bin/env python3
"""Drives a running TiXL editor (launched with --debug-server <port>) through setting up
and verifying the MediaPipe test project: VideoDeviceInput -> HandLandmarkDetection.

JSON-lines protocol over TCP 127.0.0.1, one request per line, one response per line.
"""
import json
import socket
import sys
import time

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 9042
SHOT_PATH = sys.argv[2] if len(sys.argv) > 2 else "/tmp/tixl-mediapipe/hand-output.png"

s = socket.create_connection(("127.0.0.1", PORT), timeout=10)
s.settimeout(180)  # openProject / newProject compile synchronously and can take a while
f = s.makefile("rw", encoding="utf-8", newline="\n")
_counter = 0


def call(method, **args):
    global _counter
    _counter += 1
    request = {"id": str(_counter), "method": method, **args}
    f.write(json.dumps(request) + "\n")
    f.flush()
    line = f.readline()
    if not line:
        raise RuntimeError("connection closed")
    envelope = json.loads(line)
    if not envelope.get("ok"):
        raise RuntimeError(f"{method} failed: {envelope.get('error')}")
    return envelope.get("result") or {}


def wait_for(predicate, what, timeout_s=60):
    deadline = time.time() + timeout_s
    last = None
    while time.time() < deadline:
        last = predicate()
        if last:
            return last
        call("pumpFrames", count=30)
    raise TimeoutError(f"{what} not satisfied; last={last}")


print("ping:", call("ping"))
version = call("getVersion")
print("editor:", version)

# --- project (open if it exists, create only when missing) -------------------
def try_call(method, **args):
    call.n = getattr(call, 'n', 0) + 1
    f.write(json.dumps({"id": str(call.n), "method": method, **args}) + "\n"); f.flush()
    return json.loads(f.readline())

open_response = try_call("openProject", name="_mediapipeTest")
if open_response.get("ok"):
    print("opened existing _mediapipeTest")
else:
    result = call("newProject", name="_mediapipeTest")
    print("created project:", result)
    call("openProject", name="_mediapipeTest")
call("pumpFrames", count=10)
print("project open")

# --- graph (idempotent: reuse ops from a previous run) -----------------------
def find_child(symbol_name):
    for child in call("getGraphState")["children"]:
        if child.get("symbolName") == symbol_name:
            return child["childId"]
    return None


video = find_child("VideoDeviceInput")
if video is None:
    video = call("addOp", symbolName="VideoDeviceInput", posX=0, posY=0)["childId"]
    print("added VideoDeviceInput", video)
else:
    print("VideoDeviceInput already present", video)

hand = find_child("HandLandmarkDetection")
if hand is None:
    hand = call("addOp", symbolName="HandLandmarkDetection", posX=300, posY=0)["childId"]
    print("added HandLandmarkDetection", hand)
else:
    print("HandLandmarkDetection already present", hand)

have_connection = any(c["sourceParentOrChildId"] == video and c["targetParentOrChildId"] == hand
                      for c in call("getGraphState")["connections"])
if not have_connection:
    call("connect", sourceChildId=video, sourceOutput="Texture",
         targetChildId=hand, targetInput="InputTexture")
    print("connected VideoDeviceInput.Texture -> HandLandmarkDetection.InputTexture")
else:
    print("connection already present")

# The camera only captures when Active is set (default false).
call("setInput", childId=video, inputName="Active", value=True)
print("VideoDeviceInput activated")

# Landmarker logging on so init problems show up in the log tail.
call("setInput", childId=hand, inputName="Debug", value=True)

# Pin the landmarker: the output window then renders ITS output, which pulls
# InputTexture from the camera op - without a consumer nothing evaluates and
# every UpdateCount stays 0 no matter how long we pump.
call("pin", childId=hand)
call("pumpFrames", count=10)

# --- drive -----------------------------------------------------------------
def hand_counts():
    updates = call("getOutput", childId=hand, outputName="UpdateCount")
    hands = call("getOutput", childId=hand, outputName="HandCount")
    video_updates = call("getOutput", childId=video, outputName="UpdateCount")
    return {"videoUpdates": video_updates.get("value"),
            "landmarkerUpdates": updates.get("value"),
            "hands": hands.get("value")}


def wait_for_counts(timeout_s=120):
    # Note: HandLandmarkDetection.UpdateCount is a vestigial output that is never written;
    # camera UpdateCount and the landmarker's HandCount are the live signals.
    deadline = time.time() + timeout_s
    last = None
    while time.time() < deadline:
        last = hand_counts()
        if last["videoUpdates"] not in (None, 0):
            return last
        call("pumpFrames", count=30)
    raise TimeoutError(f"camera not producing frames; last counts: {last}")


result = wait_for_counts()
print("pipeline counts:", result)
if result["hands"]:
    print(f"landmarker reports {result['hands']} hand(s) detected from the live camera")

# A few more frames, then read again - hand landmarks on a live camera need a hand in view,
# so HandCount may legitimately stay 0; UpdateCount is the pipeline-liveness proof.
call("pumpFrames", count=60)
counts = hand_counts()
print("final counts:", counts)

# --- errors in the log ------------------------------------------------------
tail = call("getLogTail", sinceSeq=-1, minLevel="warning", maxCount=100)
entries = tail.get("entries", [])
interesting = [e for e in entries
               if any(k in (e.get("message") or "") for k in
                      ("Mediapipe", "mediapipe", "HandLandmark", "VideoDevice", "Webcam", "Camera"))]
print(f"log warnings mentioning the pipeline: {len(interesting)}")
for e in interesting[:10]:
    print("  ", e.get("level"), "|", (e.get("message") or "")[:160])

# --- screenshots --------------------------------------------------------------
import os
os.makedirs(os.path.dirname(SHOT_PATH), exist_ok=True)
call("pin", childId=hand, outputName="OutputTexture")
call("pumpFrames", count=10)
passthrough = SHOT_PATH.replace(".png", "-passthrough.png")
call("screenshot", path=passthrough, target="output")
print("passthrough screenshot:", passthrough, os.path.getsize(passthrough), "bytes")
ui_shot = SHOT_PATH.replace(".png", "-editor.png")
call("screenshot", path=ui_shot, target="ui")
print("editor screenshot:", ui_shot, os.path.getsize(ui_shot), "bytes")
# Leave the debug overlay (landmarks on the camera frame) on screen.
call("pin", childId=hand, outputName="DebugTexture")
call("pumpFrames", count=20)
call("screenshot", path=SHOT_PATH, target="output")
print("debug-texture screenshot:", SHOT_PATH, os.path.getsize(SHOT_PATH), "bytes")

# --- persist ----------------------------------------------------------------
call("save")
print("project saved")
print("DONE")
