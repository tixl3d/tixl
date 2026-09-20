import json, os, pathlib, subprocess, tempfile
from concurrent.futures import ThreadPoolExecutor

REPO = pathlib.Path(__file__).resolve().parents[2]
SLANGC = os.path.expanduser("~/.local/opt/slang-2026.18/bin/slangc")
OUTPUT = pathlib.Path(__file__).resolve().parent / ".temp"
rows = [r for r in json.load(open(OUTPUT / "results.json")) if r["ok"]]

def run(item):
    index, row = item
    path = REPO / row["relative"]
    out = OUTPUT / f"val{index}.spv"
    args = [SLANGC, str(path), "-entry", row["entry"], "-stage", row["stage"], "-target", "spirv",
            "-profile", "sm_5_0", "-capability", "spirv_1_5", "-D", "sampler=SamplerState",
            "-fvk-use-dx-layout", "-fvk-s-shift", "0", "all", "-fvk-b-shift", "16", "all",
            "-fvk-t-shift", "32", "all", "-fvk-u-shift", "160", "all", "-o", str(out),
            "-I", str(REPO / "Operators/Lib/Assets/shaders"), "-I", str(path.parent)]
    subprocess.run(args, capture_output=True, cwd=REPO)

    if not out.exists():
        return row, "no output"

    result = subprocess.run(["spirv-val", str(out)], capture_output=True, text=True)
    out.unlink()
    return row, None if result.returncode == 0 else result.stdout + result.stderr

with ThreadPoolExecutor(max_workers=32) as pool:
    results = list(pool.map(run, enumerate(rows)))

bad = [(r, m) for r, m in results if m]
print(f"spirv-val: {len(results) - len(bad)}/{len(results)} clean")

for row, message in bad:
    print(f"  {row['relative']} [{row['entry']}] referenced={row['referenced']}")
    print("    " + "\n    ".join(message.strip().split("\n")[:3]))
