#!/usr/bin/env python3
"""
Compiles TiXL's HLSL shaders with Slang to SPIR-V and reports what breaks.

Survey for the Vulkan port, not part of the build. It compiles every (file, entry point, stage) that the
operator packages reference through [ComputeShader], [PixelShader], [VertexShader] and [GeometryShader],
plus unreferenced .hlsl files with guessed entry points, and checks:

- compile errors and warnings, grouped by cause
- spirv-val on every produced module
- constant buffer layouts: every member offset from the SPIR-V build against Slang's own D3D layout
  (HLSL target) and against an independent implementation of FXC's packing rules
- binding and resource statistics the Vulkan backend has to support

Usage:  python3 Spikes/ShaderSurvey/shader_survey.py [--slangc PATH] [--jobs N] [--limit N]
Output: Spikes/ShaderSurvey/.temp/report.md and results.json
"""
import argparse
import collections
import concurrent.futures
import json
import os
import pathlib
import re
import shutil
import subprocess
import time

REPO = pathlib.Path(__file__).resolve().parents[2]
OPERATORS = REPO / "Operators"
SHADER_ROOT = OPERATORS / "Lib" / "Assets" / "shaders"
GFX_TYPE_OPS = OPERATORS / "TypeOperators" / "Symbols" / "Gfx"
OUTPUT = pathlib.Path(__file__).resolve().parent / ".temp"
PINNED_SLANGC = pathlib.Path.home() / ".local/opt/slang-2026.18/bin/slangc"

# Wrapper ops that name a shader themselves and feed it to an inner shader op through a connection.
# Their entry point is fixed inside the wrapper, so it cannot be read off the child's input values.
FIXED_SHADER_OPS = {
    "bd0b9c5b-c611-42d0-8200-31af9661f189": ("fragment", "1e4e274b-60b2-4fe8-b275-ebef80d520a7", "psMain"),
    "2b20afce-2b54-4bcc-ba0e-e456a0d92833": ("fragment", "1e4e274b-60b2-4fe8-b275-ebef80d520a7", "psMain"),
}

# Shader operator symbol id -> (stage, type-op file). Input ids and defaults are read from the .t3 files.
SHADER_OPS = {
    "a256d70f-adb3-481d-a926-caf35bd3e64c": ("compute", "ComputeShader"),
    "f7c625da-fede-4993-976c-e259e0ee4985": ("fragment", "PixelShader"),
    "646f5988-0a76-4996-a538-ba48054fd0ad": ("vertex", "VertexShader"),
    "a908cc64-e8cb-490c-ae45-c2c5fbfcedfb": ("geometry", "GeometryShader"),
}

# Same binding scheme as the Vulkan spike: every register class gets a fixed range in descriptor set 0.
COMMON_ARGS = [
    "-profile", "sm_5_0",
    "-capability", "spirv_1_5",
    "-D", "sampler=SamplerState",
    "-fvk-s-shift", "0", "0",
    "-fvk-b-shift", "16", "0",
    "-fvk-t-shift", "32", "0",
    "-fvk-u-shift", "160", "0",
    "-fvk-use-dx-layout",
]

DIAGNOSTIC = re.compile(r"^(error|warning)\[(E\d+)\]: (.*)$")
LOCATION = re.compile(r"^\s*--> (.*?):(\d+):(\d+)")


# ---------------------------------------------------------------------------------------------------- usages

def load_t3(path):
    text = path.read_text(encoding="utf-8-sig")
    # .t3 files annotate ids with /*Name*/ comments, which JSON does not allow.
    text = re.sub(r'(?<=")/\*.*?\*/', "", text)
    return json.loads(text)


def read_type_op_inputs():
    """Per shader op: (source input id, entry input id, default source, default entry)."""
    result = {}
    for symbol_id, (stage, file_name) in SHADER_OPS.items():
        definition = load_t3(GFX_TYPE_OPS / f"{file_name}.t3")
        source_id = entry_id = None
        defaults = {}
        cs_text = (GFX_TYPE_OPS / f"{file_name}.cs").read_text()
        for match in re.finditer(r'Input\(Guid\s*=\s*"\{?([0-9A-Fa-f-]+)\}?"\)\]\s*public readonly InputSlot<string> (\w+)', cs_text):
            guid, name = match.group(1).lower(), match.group(2)
            if name == "Source":
                source_id = guid
            elif name == "EntryPoint":
                entry_id = guid
        for slot in definition.get("Inputs", []):
            defaults[slot["Id"].lower()] = slot.get("DefaultValue")
        result[symbol_id] = (stage, source_id, entry_id, defaults.get(source_id), defaults.get(entry_id))
    return result


def collect_usages(type_ops):
    usages = collections.defaultdict(list)  # (address, entry, stage) -> [t3 files]
    dynamic_sources = []
    unreadable = []
    for t3_path in OPERATORS.rglob("*.t3"):
        if ".temp" in t3_path.parts:
            continue
        try:
            symbol = load_t3(t3_path)
        except (json.JSONDecodeError, UnicodeDecodeError) as error:
            unreadable.append(f"{t3_path.relative_to(REPO)}: {error}")
            continue

        connected = {(c["TargetParentOrChildId"].lower(), c["TargetSlotId"].lower()) for c in symbol.get("Connections", [])}
        for child in symbol.get("Children", []):
            symbol_id = child["SymbolId"].lower()
            fixed = FIXED_SHADER_OPS.get(symbol_id)
            if fixed is not None:
                stage, source_id, entry = fixed
                values = {v["Id"].lower(): v.get("Value") for v in child.get("InputValues", [])}
                if (child["Id"].lower(), source_id) in connected:
                    dynamic_sources.append(str(t3_path.relative_to(REPO)))
                else:
                    usages[(values.get(source_id), entry, stage)].append(str(t3_path.relative_to(REPO)))
                continue

            op = type_ops.get(symbol_id)
            if op is None:
                continue
            stage, source_id, entry_id, default_source, default_entry = op
            values = {v["Id"].lower(): v.get("Value") for v in child.get("InputValues", [])}
            child_id = child["Id"].lower()
            if (child_id, source_id) in connected or (child_id, entry_id) in connected:
                dynamic_sources.append(str(t3_path.relative_to(REPO)))
                continue
            address = values.get(source_id, default_source)
            entry = values.get(entry_id, default_entry)
            usages[(address, entry, stage)].append(str(t3_path.relative_to(REPO)))
    return usages, dynamic_sources, unreadable


SHADER_ADDRESS = re.compile(r"^\w+:.*\.hlsl$", re.IGNORECASE)


def collect_named_shaders():
    """Every shader address any .t3 stores, whatever operator holds it.

    collect_usages only understands the shader ops it knows by id, and TiXL has several wrappers that
    name a shader themselves. This catches the rest; the entry points then come from the naming
    convention, which over- rather than under-reports what the editor needs.
    """
    named = collections.defaultdict(list)
    for t3_path in OPERATORS.rglob("*.t3"):
        if ".temp" in t3_path.parts:
            continue
        try:
            symbol = load_t3(t3_path)
        except (json.JSONDecodeError, UnicodeDecodeError):
            continue
        for child in symbol.get("Children", []):
            for value in child.get("InputValues", []):
                text = value.get("Value")
                if isinstance(text, str) and SHADER_ADDRESS.match(text):
                    named[text].append(str(t3_path.relative_to(REPO)))
    return named


def resolve_address(address):
    """'Lib:shaders/x.hlsl' -> Operators/Lib/Assets/shaders/x.hlsl, or None for other forms."""
    if not address or ":" not in address:
        return None
    package, relative = address.split(":", 1)
    for folder in OPERATORS.iterdir():
        if folder.name.lower() == package.lower():
            return folder / "Assets" / relative
    return None


def guess_entries(path):
    """Entry points of an unreferenced file, from TiXL's naming conventions."""
    text = path.read_text(encoding="utf-8", errors="replace")
    entries = [(name, "compute") for name in re.findall(r"\[numthreads\s*\([^)]*\)\]\s*void\s+(\w+)\s*\(", text)]
    for name, stage in (("vsMain", "vertex"), ("psMain", "fragment"), ("gsMain", "geometry")):
        if re.search(rf"\b{name}\s*\(", text):
            entries.append((name, stage))
    return entries


# ---------------------------------------------------------------------------------------------------- compile

def compile_entry(slangc, job_index, path, entry, stage):
    stem = f"{job_index:04d}"
    spirv_path = OUTPUT / "spv" / f"{stem}.spv"
    reflection_path = OUTPUT / "spv" / f"{stem}.json"
    arguments = [slangc, str(path), "-entry", entry, "-stage", stage, "-target", "spirv", "-I", str(SHADER_ROOT),
                 *COMMON_ARGS, "-o", str(spirv_path), "-reflection-json", str(reflection_path)]
    started = time.perf_counter()
    process = subprocess.run(arguments, capture_output=True, text=True)
    seconds = time.perf_counter() - started
    result = {
        "ok": process.returncode == 0 and spirv_path.exists(),
        "seconds": seconds,
        "diagnostics": parse_diagnostics(process.stderr + process.stdout),
        "raw": (process.stderr + process.stdout)[-4000:],
    }
    if not result["ok"]:
        return result

    validation = subprocess.run(["spirv-val", "--target-env", "vulkan1.3", "--scalar-block-layout", str(spirv_path)], capture_output=True, text=True)
    result["spirv_val"] = validation.stdout.strip() + validation.stderr.strip() if validation.returncode else ""
    result["spirv_reflection"] = json.loads(reflection_path.read_text())

    # Slang's own D3D layout, as a second opinion on every cbuffer offset.
    hlsl_reflection = OUTPUT / "spv" / f"{stem}.hlsl.json"
    hlsl = subprocess.run([slangc, str(path), "-entry", entry, "-stage", stage, "-target", "hlsl", "-I", str(SHADER_ROOT),
                           "-profile", "sm_5_0", "-D", "sampler=SamplerState",
                           "-o", str(OUTPUT / "spv" / f"{stem}.hlsl"), "-reflection-json", str(hlsl_reflection)],
                          capture_output=True, text=True)
    if hlsl.returncode == 0 and hlsl_reflection.exists():
        result["hlsl_reflection"] = json.loads(hlsl_reflection.read_text())
    return result


def parse_diagnostics(text):
    diagnostics = []
    current = None
    for line in text.splitlines():
        match = DIAGNOSTIC.match(line.strip())
        if match:
            current = {"severity": match.group(1), "code": match.group(2), "message": match.group(3), "file": None, "line": None}
            diagnostics.append(current)
            continue
        location = LOCATION.match(line)
        if location and current is not None and current["file"] is None:
            current["file"], current["line"] = location.group(1), int(location.group(2))
    return diagnostics


def normalize_message(message):
    """Turns 'undefined identifier 'foo'' into 'undefined identifier <name>' so causes group together."""
    return re.sub(r"'[^']*'", "<name>", message)


# ---------------------------------------------------------------------------------------------------- cbuffer layout

def fxc_size_and_align(type_info):
    """Byte size of a member under FXC's cbuffer packing rules, and whether it must start a new register."""
    kind = type_info["kind"]
    if kind == "scalar":
        return (8 if type_info["scalarType"] in ("float64", "int64", "uint64") else 4), False
    if kind == "vector":
        element_size, _ = fxc_size_and_align(type_info["elementType"])
        return element_size * type_info["elementCount"], False
    if kind == "matrix":
        element_size, _ = fxc_size_and_align(type_info["elementType"])
        rows, columns = type_info["rowCount"], type_info["columnCount"]
        # Default column_major: one register per column holding `rows` components.
        return 16 * (columns - 1) + element_size * rows, True
    if kind == "array":
        element_size, _ = fxc_size_and_align(type_info["elementType"])
        count = type_info["elementCount"]
        stride = (element_size + 15) // 16 * 16
        return stride * (count - 1) + element_size, True
    if kind == "struct":
        return fxc_struct_size(type_info["fields"]), True
    raise ValueError(f"unhandled type kind {kind}")


def fxc_layout(fields, base=0):
    """Offsets FXC assigns to struct or cbuffer members, flattened as 'Outer.Inner' -> offset."""
    offsets = {}
    offset = 0
    for field in fields:
        size, new_register = fxc_size_and_align(field["type"])
        if new_register or (offset % 16) + size > 16:
            offset = (offset + 15) // 16 * 16
        offset = (offset + 3) // 4 * 4
        offsets[field["name"]] = base + offset
        if field["type"]["kind"] == "struct":
            for name, nested in fxc_layout(field["type"]["fields"], base + offset).items():
                offsets[f"{field['name']}.{name}"] = nested
        offset += size
    return offsets


def fxc_struct_size(fields):
    offset = 0
    for field in fields:
        size, new_register = fxc_size_and_align(field["type"])
        if new_register or (offset % 16) + size > 16:
            offset = (offset + 15) // 16 * 16
        offset = (offset + 3) // 4 * 4 + size
    return offset


def reflected_layout(fields, base=0):
    offsets = {}
    for field in fields:
        offset = base + field["binding"]["offset"]
        offsets[field["name"]] = offset
        if field["type"]["kind"] == "struct":
            offsets.update({f"{field['name']}.{k}": v for k, v in reflected_layout(field["type"]["fields"], offset).items()})
    return offsets


def cbuffers(reflection):
    """name -> fields for every constant buffer parameter."""
    result = {}
    for parameter in reflection.get("parameters", []):
        type_info = parameter.get("type", {})
        if type_info.get("kind") == "constantBuffer":
            element = type_info.get("elementType", {})
            if element.get("kind") == "struct":
                result[parameter["name"]] = element["fields"]
    return result


def compare_cbuffers(result):
    mismatches = []
    spirv = cbuffers(result["spirv_reflection"])
    hlsl = cbuffers(result.get("hlsl_reflection", {}))
    for name, fields in spirv.items():
        spirv_offsets = reflected_layout(fields)
        fxc_offsets = fxc_layout(fields)
        hlsl_offsets = reflected_layout(hlsl[name]) if name in hlsl else {}
        for member, offset in spirv_offsets.items():
            expected_fxc = fxc_offsets.get(member)
            expected_hlsl = hlsl_offsets.get(member)
            if offset != expected_fxc or (hlsl_offsets and offset != expected_hlsl):
                mismatches.append({"cbuffer": name, "member": member, "spirv": offset, "fxc_rules": expected_fxc, "slang_hlsl": expected_hlsl})
    return mismatches, len(spirv)


# ---------------------------------------------------------------------------------------------------- statistics

def binding_statistics(reflection):
    stats = []
    for parameter in reflection.get("parameters", []):
        binding = parameter.get("binding", {})
        type_info = parameter.get("type", {})
        kind = binding.get("kind")
        index = binding.get("index")
        if kind is None or index is None:
            continue
        resource = type_info.get("kind")
        shape = type_info.get("baseShape") or type_info.get("kind")
        access = type_info.get("access", "")
        stats.append({"kind": kind, "index": index, "resource": resource, "shape": shape, "access": access,
                      "name": parameter.get("name")})
    return stats


# ---------------------------------------------------------------------------------------------------- report

def write_report(jobs, results, dynamic_sources, unreadable, missing_files, total_seconds):
    lines = []
    add = lines.append

    referenced = [j for j in jobs if j["referenced"]]
    guessed = [j for j in jobs if not j["referenced"]]

    def summary(subset):
        ok = sum(1 for j in subset if results[j["index"]]["ok"])
        return ok, len(subset)

    ok_ref, total_ref = summary(referenced)
    ok_guess, total_guess = summary(guessed)
    all_seconds = sorted(results[j["index"]]["seconds"] for j in jobs)

    add("# Slang shader survey")
    add("")
    add(f"slangc: `{SLANGC_VERSION}` · flags: `{' '.join(COMMON_ARGS)}` · include root: `Lib:shaders/`")
    add("")
    add("## Summary")
    add("")
    add("| | Compiled | Total |")
    add("|---|---|---|")
    add(f"| Entry points referenced by operators | {ok_ref} | {total_ref} |")
    add(f"| Guessed entry points in unreferenced files | {ok_guess} | {total_guess} |")
    add("")
    add(f"- Referenced shader files missing on disk: {len(missing_files)}")
    add(f"- Shader ops with a connected (generated) source, not surveyed: {len(dynamic_sources)}")
    add(f"- Unreadable .t3 files: {len(unreadable)}")
    if all_seconds:
        median = all_seconds[len(all_seconds) // 2]
        add(f"- Compile time per entry point (SPIR-V): median {median * 1000:.0f} ms, max {all_seconds[-1] * 1000:.0f} ms, "
            f"wall clock for the whole survey {total_seconds:.0f} s")
    add("")

    # Failure causes
    causes = collections.defaultdict(list)
    for job in jobs:
        result = results[job["index"]]
        if result["ok"]:
            continue
        errors = [d for d in result["diagnostics"] if d["severity"] == "error"]
        if not errors:
            causes[("?", "failed without a parsable diagnostic")].append((job, None))
            continue
        first = errors[0]
        causes[(first["code"], normalize_message(first["message"]))].append((job, first))

    add("## Failure causes")
    add("")
    add("Grouped by the first error of each failing entry point. `ref` counts entry points operators use.")
    add("")
    for (code, message), items in sorted(causes.items(), key=lambda item: -len(item[1])):
        ref_count = sum(1 for job, _ in items if job["referenced"])
        add(f"### {code}: {message} — {len(items)} ({ref_count} ref)")
        add("")
        for job, diagnostic in items[:12]:
            where = f"{diagnostic['file']}:{diagnostic['line']}" if diagnostic and diagnostic["file"] else job["relative"]
            where = where.replace(str(REPO) + "/", "")
            detail = diagnostic["message"] if diagnostic else results[job["index"]]["raw"].strip().splitlines()[-1:]
            add(f"- `{job['relative']}` `{job['entry']}` ({job['stage']}{'' if job['referenced'] else ', guessed'}) — {where}: {detail}")
        if len(items) > 12:
            add(f"- … {len(items) - 12} more")
        add("")

    # Warnings
    warnings = collections.Counter()
    for job in jobs:
        for diagnostic in results[job["index"]]["diagnostics"]:
            if diagnostic["severity"] == "warning":
                warnings[(diagnostic["code"], normalize_message(diagnostic["message"]))] += 1
    add("## Warnings")
    add("")
    for (code, message), count in warnings.most_common(20):
        add(f"- {code} ×{count}: {message}")
    add("")

    # spirv-val
    invalid = [(job, results[job["index"]]["spirv_val"]) for job in jobs if results[job["index"]].get("spirv_val")]
    add(f"## spirv-val failures: {len(invalid)}")
    add("")
    for job, message in invalid[:20]:
        add(f"- `{job['relative']}` `{job['entry']}`: {message.splitlines()[0] if message else ''}")
    add("")

    # cbuffer layout
    layout_issues = []
    cbuffer_count = 0
    for job in jobs:
        result = results[job["index"]]
        if not result["ok"]:
            continue
        mismatches, count = compare_cbuffers(result)
        cbuffer_count += count
        for mismatch in mismatches:
            layout_issues.append((job, mismatch))
    add(f"## Constant buffer layout: {len(layout_issues)} mismatching members in {cbuffer_count} cbuffers")
    add("")
    add("Each member's SPIR-V offset against FXC's packing rules (independent implementation) and against Slang's HLSL-target layout.")
    add("")
    if layout_issues:
        add("| Shader | Entry | cbuffer.member | SPIR-V | FXC rules | Slang HLSL |")
        add("|---|---|---|---|---|---|")
        for job, m in layout_issues[:60]:
            add(f"| `{job['relative']}` | `{job['entry']}` | {m['cbuffer']}.{m['member']} | {m['spirv']} | {m['fxc_rules']} | {m['slang_hlsl']} |")
        add("")

    # Bindings
    max_index = collections.defaultdict(int)
    resource_shapes = collections.Counter()
    for job in jobs:
        result = results[job["index"]]
        if not result["ok"]:
            continue
        for stat in binding_statistics(result["spirv_reflection"]):
            max_index[stat["kind"]] = max(max_index[stat["kind"]], stat["index"])
            resource_shapes[f"{stat['kind']} {stat['shape']} {stat['access']}".strip()] += 1
    add("## Bindings")
    add("")
    add("Highest binding per class (after the register shifts: s 0–15, b 16–31, t 32–159, u 160+):")
    add("")
    for kind, index in sorted(max_index.items()):
        add(f"- {kind}: {index}")
    add("")
    add("Resource kinds across all compiled entry points:")
    add("")
    for shape, count in resource_shapes.most_common():
        add(f"- {shape}: {count}")
    add("")

    if missing_files:
        add("## Referenced but missing shader files")
        add("")
        for address, users in sorted(missing_files.items()):
            add(f"- `{address}` (used by {users[0]}{' and more' if len(users) > 1 else ''})")
        add("")

    if unreadable:
        add("## Unreadable .t3 files")
        add("")
        for line in unreadable:
            add(f"- {line}")
        add("")

    (OUTPUT / "report.md").write_text("\n".join(lines))


# ---------------------------------------------------------------------------------------------------- main

SLANGC_VERSION = "?"


def main():
    global SLANGC_VERSION
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--slangc", default=os.environ.get("TIXL_SLANGC") or (str(PINNED_SLANGC) if PINNED_SLANGC.exists() else "slangc"))
    parser.add_argument("--jobs", type=int, default=os.cpu_count())
    parser.add_argument("--limit", type=int, default=0, help="compile only the first N entry points (for quick runs)")
    arguments = parser.parse_args()

    SLANGC_VERSION = subprocess.run([arguments.slangc, "-version"], capture_output=True, text=True).stdout.strip() or \
                     subprocess.run([arguments.slangc, "-version"], capture_output=True, text=True).stderr.strip()
    if OUTPUT.exists():
        shutil.rmtree(OUTPUT)
    (OUTPUT / "spv").mkdir(parents=True)

    type_ops = read_type_op_inputs()
    usages, dynamic_sources, unreadable = collect_usages(type_ops)

    jobs = []
    missing_files = collections.defaultdict(list)
    referenced_files = set()
    for (address, entry, stage), users in sorted(usages.items(), key=lambda item: str(item[0])):
        path = resolve_address(address)
        if path is None or not path.exists():
            missing_files[str(address)].extend(users)
            continue
        referenced_files.add(path.resolve())
        jobs.append({"path": path, "entry": entry, "stage": stage, "referenced": True, "users": users})

    named_files = {}
    for address, users in collect_named_shaders().items():
        path = resolve_address(address)
        if path is not None and path.exists():
            named_files[path.resolve()] = users

    for path in sorted(OPERATORS.rglob("*.hlsl")):
        if ".temp" in path.parts or path.resolve() in referenced_files:
            continue
        users = named_files.get(path.resolve(), [])
        for entry, stage in guess_entries(path):
            jobs.append({"path": path, "entry": entry, "stage": stage, "referenced": bool(users), "users": users})

    if arguments.limit:
        jobs = jobs[:arguments.limit]
    for index, job in enumerate(jobs):
        job["index"] = index
        job["relative"] = str(job["path"].relative_to(REPO))

    print(f"{len(jobs)} entry points ({sum(j['referenced'] for j in jobs)} referenced), {arguments.jobs} parallel compiles")
    started = time.perf_counter()
    results = {}
    with concurrent.futures.ThreadPoolExecutor(arguments.jobs) as pool:
        futures = {pool.submit(compile_entry, arguments.slangc, j["index"], j["path"], j["entry"], j["stage"]): j for j in jobs}
        for done, future in enumerate(concurrent.futures.as_completed(futures), 1):
            results[futures[future]["index"]] = future.result()
            if done % 100 == 0:
                print(f"  {done}/{len(jobs)}")
    total_seconds = time.perf_counter() - started

    write_report(jobs, results, dynamic_sources, unreadable, missing_files, total_seconds)
    serializable = [{**{k: v for k, v in j.items() if k != "path"},
                     **{k: v for k, v in results[j["index"]].items() if k not in ("spirv_reflection", "hlsl_reflection")}}
                    for j in jobs]
    (OUTPUT / "results.json").write_text(json.dumps(serializable, indent=1))
    ok = sum(1 for r in results.values() if r["ok"])
    print(f"{ok}/{len(jobs)} compiled in {total_seconds:.0f} s — report: {OUTPUT / 'report.md'}")


if __name__ == "__main__":
    main()
