"""Add a 3D-scanning setup to the Test project's home symbol.

Wires: VideoStreamInput -> DepthAnything (Da3Base, CUDA) -> DepthScanBuffer + DepthScan3D
       DepthScanBuffer.PointCloud -> existing DrawPoints (camera-space preview)
       DepthScan3D.Points -> new DrawPoints (world-space cloud preview)

Idempotent: re-running replaces the nodes added by a previous run.
Run:  python -I Scripts/add_3dscan_setup.py
"""
import json, re

PROJ = r"C:\Users\Artonurban\Documents\TiXL4.4-alpha\Test"
T3 = PROJ + r"\Symbols\Test.t3"
T3UI = PROJ + r"\Symbols\Test.t3ui"

# --- pin GUIDs (verified against operator sources) ---
DA_CHILD = "1006f5dd-eaa8-411a-88db-1589e7432fe1"      # DepthAnything instance
BUF_CHILD = "459ea3eb-1911-4707-83b3-b75d0c9f7730"     # DepthScanBuffer instance
SCAN3D_SYM = "7e6eb18c-433f-41c0-8443-6a82dae72703"
SCAN3D_CHILD = "3d5ca7f1-8b24-4c6a-9e35-2f1d0a9b7c44"
DP_SYM = "ffd94e5a-bc98-4e70-84d8-cce831e6925f"        # Lib.point.draw.DrawPoints
DP_WORLD_CHILD = "7e9b2d04-6c15-4a8f-b3e7-5c8d1f2a6e93"

DA_CAMREF = "5a3334ec-78c3-4001-8018-3d8aae7f870f"     # DepthAnything.CamReference out
DA_DEPTH = "e0f9e8b7-6d5c-4e3f-1a0f-9c8b7a6d5e4f"      # DepthAnything.DepthTexture out
DA_COLOR = "d9e8f7a6-5c4b-3d2e-0f9e-8b7a6c5d4e3f"      # DepthAnything.OutputTexture out
SCAN3D_DEPTHMAP = "93c8d941-f395-4271-b36a-86aadb010cff"
SCAN3D_COLORMAP = "afc191c7-be4d-44eb-938b-701e35f75fae"
SCAN3D_CAMREF_IN = "a7a2eb05-619f-4363-9b6a-120d3c4d0411"
SCAN3D_POINTS_OUT = "9165831a-1b50-44f2-a5e3-c88c5e630423"
SCAN3D_MAXPOINTS_IN = "5304c51a-4e3f-4d9c-a34b-4af6f5cbe0d9"
BUF_CAMREF_IN = "c8d9e0f1-a2b3-4c5d-8e6f-7a8b9c0d1e2f"
BUF_CAPTURE_IN = "dc882e31-4963-4c64-8a56-1cca9af6b362"
DP_GPOINTS_IN = "5df18658-ef86-4c0f-8bb4-4ac3fbbf9a33"

NEW_CHILDREN = [SCAN3D_CHILD, DP_WORLD_CHILD]

# connections: (srcChild, srcSlot, dstChild, dstSlot)
NEW_CONNECTIONS = [
    (DA_CHILD, DA_CAMREF, BUF_CHILD, BUF_CAMREF_IN),        # pose also to the camera-space buffer
    (DA_CHILD, DA_CAMREF, SCAN3D_CHILD, SCAN3D_CAMREF_IN),  # pose -> world-space fusion
    (DA_CHILD, DA_DEPTH, SCAN3D_CHILD, SCAN3D_DEPTHMAP),
    (DA_CHILD, DA_COLOR, SCAN3D_CHILD, SCAN3D_COLORMAP),
    (SCAN3D_CHILD, SCAN3D_POINTS_OUT, DP_WORLD_CHILD, DP_GPOINTS_IN),
]

POSITIONS = {
    SCAN3D_CHILD: (-2250.0, -3900.0),
    DP_WORLD_CHILD: (-2050.0, -3900.0),
}

NAMES = {
    SCAN3D_CHILD: "DepthScan3D",
    DP_WORLD_CHILD: "DrawPointsWorld",
    SCAN3D_MAXPOINTS_IN: "MaxPoints",
}


def load(path):
    raw = open(path, encoding="utf-8").read()
    return json.loads(re.sub(r'/\*.*?\*/', '', raw, flags=re.S)), raw


def dump_with_comments(obj, name_map):
    text = json.dumps(obj, indent=2)
    for g, n in name_map.items():
        text = re.sub(rf'("Id": "{g}")(?!/\*)', rf'\1/*{n}*/', text)
    return text + "\n"


def con_key(c):
    return (c["SourceParentOrChildId"], c["SourceSlotId"],
            c["TargetParentOrChildId"], c["TargetSlotId"])


t3, t3_raw = load(T3)
t3ui, t3ui_raw = load(T3UI)

# guid->name maps from the original /*Name*/ comments
t3_map = dict(re.findall(r'"Id": "([0-9a-fA-F-]+)"/\*(\w+)\*/', t3_raw))
t3_map.update(NAMES)
t3ui_map = dict(re.findall(r'"Id": "([0-9a-fA-F-]+)"/\*(\w+)\*/', t3ui_raw))

# ---- idempotency: drop previously-added children, their connections and UIs ----
t3["Children"] = [c for c in t3["Children"] if c["Id"] not in NEW_CHILDREN]
t3["Connections"] = [c for c in t3["Connections"]
                     if c["SourceParentOrChildId"] not in NEW_CHILDREN
                     and c["TargetParentOrChildId"] not in NEW_CHILDREN]
t3ui["SymbolChildUis"] = [u for u in t3ui["SymbolChildUis"] if u["ChildId"] not in NEW_CHILDREN]

# ---- DepthScanBuffer: Capture True -> False (hold-to-capture off; use StartRecord) ----
for child in t3["Children"]:
    if child["Id"] == BUF_CHILD:
        for iv in child.get("InputValues", []):
            if iv["Id"] == BUF_CAPTURE_IN:
                iv["Value"] = False

# ---- add the two children ----
t3["Children"].append({
    "Id": SCAN3D_CHILD,
    "SymbolId": SCAN3D_SYM,
    "SymbolName": "Lib.io.video.depthanything.DepthScan3D",
    "InputValues": [
        {"Id": SCAN3D_MAXPOINTS_IN, "Type": "System.Int32", "Value": 1000000},
    ],
    "Outputs": [],
})
t3["Children"].append({
    "Id": DP_WORLD_CHILD,
    "SymbolId": DP_SYM,
    "SymbolName": "Lib.point.draw.DrawPoints",
    "InputValues": [],
    "Outputs": [],
})

# ---- add connections ----
existing_cons = {con_key(c) for c in t3["Connections"]}
for src_c, src_s, dst_c, dst_s in NEW_CONNECTIONS:
    if (src_c, src_s, dst_c, dst_s) not in existing_cons:
        t3["Connections"].append({
            "SourceParentOrChildId": src_c,
            "SourceSlotId": src_s,
            "TargetParentOrChildId": dst_c,
            "TargetSlotId": dst_s,
        })

# ---- t3ui positions ----
for child_id, (x, y) in POSITIONS.items():
    t3ui["SymbolChildUis"].append({
        "ChildId": child_id,
        "SymbolName": ("Lib.io.video.depthanything.DepthScan3D"
                       if child_id == SCAN3D_CHILD else "Lib.point.draw.DrawPoints"),
        "Position": {"X": x, "Y": y},
    })

# ---- validate + write ----
t3_text = dump_with_comments(t3, t3_map)
t3ui_text = dump_with_comments(t3ui, t3ui_map)
for label, text in (("Test.t3", t3_text), ("Test.t3ui", t3ui_text)):
    json.loads(re.sub(r'/\*.*?\*/', '', text, flags=re.S))  # raises on broken JSON

open(T3, "w", encoding="utf-8", newline="\n").write(t3_text)
open(T3UI, "w", encoding="utf-8", newline="\n").write(t3ui_text)
print("OK: children=%d connections=%d childUis=%d"
      % (len(t3["Children"]), len(t3["Connections"]), len(t3ui["SymbolChildUis"])))
