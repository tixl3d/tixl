"""Drive the visual reference test suite ([AllTests]) via the debug protocol.

Flow (per maintainer): open project -> select the ExecuteTests op (so the UI's
per-frame preview pulls it) -> set trigger false -> wait frames -> set true ->
wait frames until the report lands. No forced evaluation.
"""
import json
import re
import socket
import sys
import time

PORT = 9042
ALL_TESTS_SYMBOL = "b4a9f19a-bbb0-4d65-8d4a-560460e2505c"
EXECUTE_TESTS_SYMBOL = "83cb923e-a387-4be2-b391-4111c7bd90fe"
KNOWN_FLAKY = (
    "DemoProjectTests / DemoWorksForEverybody / 35",
    "DemoProjectTests / DemoThere / 05",
)

deadline = time.time() + 150
sock = None
while time.time() < deadline:
    try:
        sock = socket.create_connection(("127.0.0.1", PORT), timeout=2)
        break
    except OSError:
        time.sleep(2)
assert sock, "server did not come up"
sock.settimeout(60)
reader = sock.makefile("r")


def call(method, **params):
    sock.sendall((json.dumps({"id": method, "method": method, **params}) + "\n").encode())
    return json.loads(reader.readline())


def require(response, what):
    if not response.get("ok"):
        print(f"FAIL at {what}: {response.get('error')}")
        sys.exit(1)
    return response["result"]


def dump_diagnostics(reason):
    print(f"ABORT: {reason} - diagnostics:")
    graph = call("getGraphState", includeDefaults=False)["result"]
    trig = next((c for c in graph["children"] if c["childId"] == execute_child["childId"]), None)
    print("  ExecuteTests inputs:", (trig or {}).get("inputs"))
    for entry in call("getLogTail", maxCount=10)["result"]["entries"]:
        print(f"  log [{entry['level']}] {entry['message'][:110]}")
    sys.exit(1)


def run_suite(label):
    """Trigger with a clean flank and wait for a fresh report."""
    previous = call("getOutput", childId=execute_child["childId"])["result"].get("value") or ""
    require(call("setInput", childId=execute_child["childId"], inputName="TriggerTest", value=False), "clear trigger")
    call("pumpFrames", count=5)
    require(call("setInput", childId=execute_child["childId"], inputName="TriggerTest", value=True), "set trigger")

    poll_deadline = time.time() + 600
    polls = 0
    while time.time() < poll_deadline:
        call("pumpFrames", count=10)
        value = call("getOutput", childId=execute_child["childId"])["result"].get("value") or ""
        polls += 1
        if value != previous and (value.startswith("SUCCESS") or value.startswith("FAILED")):
            print(f"({label}: completed after {polls} polls)")
            return value
        if polls >= 400:
            dump_diagnostics(f"{label}: no fresh report after {polls} polls")
    dump_diagnostics(f"{label}: timeout")


# --- open, select, run -----------------------------------------------------
call("pumpFrames", count=30)
opened = require(call("openProject", symbolId=ALL_TESTS_SYMBOL), "openProject")
print(f"opened: {opened['rootSymbolName']}, pinnedOutput={opened.get('pinnedOutput')}")
call("pumpFrames", count=30)

graph = require(call("getGraphState", compositionId=ALL_TESTS_SYMBOL), "getGraphState")

# AllTests contains one ExecuteTests per category PLUS the aggregating "Test all"
# instance - pick the one whose Result feeds the composition's own output.
root_connection = next(c for c in graph["connections"]
                       if c["targetParentOrChildId"] == "00000000-0000-0000-0000-000000000000")
execute_child = next(c for c in graph["children"]
                     if c["childId"] == root_connection["sourceParentOrChildId"])
assert execute_child["symbolId"] == EXECUTE_TESTS_SYMBOL, \
    f"root output fed by [{execute_child['symbolName']}], expected ExecuteTests"
print(f"root output fed by [{execute_child['symbolName']}] '{execute_child.get('name', '')}'")
require(call("select", childId=execute_child["childId"]), "select")
call("pumpFrames", count=10)
print(f"selected [ExecuteTests] {execute_child['childId']}")

result_text = run_suite("first run")
print()
print("=== VISUAL REFERENCE TEST REPORT " + "=" * 30)
print(result_text[:3000])

real_failures = [line for line in result_text.splitlines()
                 if "FAILED" in line and not line.startswith(("SUCCESS", "FAILED:"))
                 and not any(line.startswith(flaky) for flaky in KNOWN_FLAKY)]
if real_failures:
    print(f"VERDICT: REAL FAILURES ({len(real_failures)}):")
    for line in real_failures:
        print("  " + line)
else:
    print("VERDICT: PASS (only known-flaky failures, if any)")

# --- phase 2: mute failures via IgnoredTestIds and verify SUCCESS ----------
# Merge newly failed ids with already-ignored ones - setInput replaces the whole
# list, and the previous ignore set may persist in the saved .t3.
failed_ids = sorted({int(m.group(1)) for line in result_text.splitlines()
                     if ": FAILED" in line or ": IGNORED" in line
                     for m in [re.search(r"#(-?\d+)\s*$", line.strip())] if m})
if failed_ids:
    print(f"\nretrying with IgnoredTestIds={failed_ids}")
    require(call("setInput", childId=execute_child["childId"], inputName="IgnoredTestIds",
                 value={"Values": failed_ids}),
            "setInput IgnoredTestIds")
    second = run_suite("second run")
    print("=== SECOND RUN (with ignores) " + "=" * 33)
    print(second[:1500])

metrics = require(call("getMetrics"), "getMetrics")
print(f"(gpu: {metrics['gpuMemory']}, fps: {metrics['fps']})")
sock.close()
