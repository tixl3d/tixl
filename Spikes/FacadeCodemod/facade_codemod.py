#!/usr/bin/env python3
"""Rewrites C# sources from SharpDX's D3D11 types onto the graphics facade.

The mechanical part of the port: using lines, fully-qualified names and the interop value types. What it
cannot do it leaves alone, so the compiler points at the rest — the state operators, the ImGui renderer and
anything touching WIC, D3DCompiler or Direct2D, which are separate problems from the facade.

    python3 Spikes/FacadeCodemod/facade_codemod.py Core Operators/Lib      # rewrite in place
    python3 Spikes/FacadeCodemod/facade_codemod.py --dry-run Editor        # report only
"""

import argparse
import pathlib
import re
import sys

# Namespaces that stay on SharpDX: image loading, shader compilation, text and the Windows helpers are not
# what the facade replaces.
UNTOUCHED_NAMESPACES = ("SharpDX.WIC", "SharpDX.D3DCompiler", "SharpDX.Direct2D1", "SharpDX.DirectWrite",
                        "SharpDX.Windows", "SharpDX.IO", "SharpDX.XInput", "SharpDX.MediaFoundation",
                        "SharpDX.Multimedia")

USING_REPLACEMENTS = {
    "using SharpDX.Direct3D11;": "using T3.Graphics.Compat;",
    "using SharpDX.Direct3D;": "using T3.Graphics.Compat;",
    "using SharpDX.DXGI;": "using T3.Graphics;\nusing T3.Graphics.Compat;",
    "using SharpDX.Mathematics.Interop;": "using System.Numerics;",
    "using SharpDX;": "using T3.Graphics.Compat;",
}

# Qualified names, longest first so a prefix never eats a longer match.
QUALIFIED_REPLACEMENTS = [
    ("SharpDX.Mathematics.Interop.RawColor4", "System.Numerics.Vector4"),
    ("SharpDX.Mathematics.Interop.RawVector4", "System.Numerics.Vector4"),
    ("SharpDX.Mathematics.Interop.RawVector3", "System.Numerics.Vector3"),
    ("SharpDX.Mathematics.Interop.RawVector2", "System.Numerics.Vector2"),
    ("SharpDX.Mathematics.Interop.RawMatrix", "System.Numerics.Matrix4x4"),
    ("SharpDX.Mathematics.Interop.RawViewportF", "T3.Graphics.Viewport"),
    ("SharpDX.Mathematics.Interop.RawRectangle", "T3.Graphics.ScissorRect"),
    ("SharpDX.Direct3D11.DeviceContext", "T3.Graphics.Compat.DeviceContext"),
    ("SharpDX.Direct3D11.Device", "T3.Graphics.Compat.Device"),
    ("SharpDX.Direct3D11.", "T3.Graphics.Compat."),
    ("SharpDX.Direct3D.", "T3.Graphics.Compat."),
    ("SharpDX.DXGI.Format", "T3.Graphics.Format"),
    ("SharpDX.DXGI.SampleDescription", "T3.Graphics.SampleDescription"),
    ("SharpDX.DataStream", "T3.Graphics.Compat.DataStream"),
    ("SharpDX.DataBox", "T3.Graphics.Compat.DataBox"),
    ("SharpDX.DataRectangle", "T3.Graphics.Compat.DataRectangle"),
    ("SharpDX.Utilities", "T3.Graphics.Compat.Utilities"),
]

# Bare type names, once the using lines point at the facade.
BARE_REPLACEMENTS = [
    ("RawColor4", "Vector4"),
    ("RawVector4", "Vector4"),
    ("RawVector3", "Vector3"),
    ("RawVector2", "Vector2"),
    ("RawMatrix", "Matrix4x4"),
    ("RawViewportF", "Viewport"),
    ("ViewportF", "Viewport"),
    ("RawRectangle", "ScissorRect"),
    ("RawBool", "bool"),
]


def rewrite(text: str) -> tuple[str, list[str]]:
    notes = []

    for old, new in QUALIFIED_REPLACEMENTS:
        if old in text:
            text = text.replace(old, new)

    lines = text.split("\n")
    result = []

    for line in lines:
        stripped = line.strip()

        if stripped in USING_REPLACEMENTS:
            for replacement in USING_REPLACEMENTS[stripped].split("\n"):
                result.append(replacement)

            continue

        # `using Device = SharpDX.Direct3D11.Device;` and friends already went through the qualified pass.
        result.append(line)

    text = "\n".join(result)

    for old, new in BARE_REPLACEMENTS:
        text = re.sub(rf"\b{old}\b", new, text)

    # Duplicate usings are easy to produce and never wanted.
    text = drop_duplicate_usings(text)

    for namespace in UNTOUCHED_NAMESPACES:
        if namespace in text:
            notes.append(f"still uses {namespace}")

    if re.search(r"\bSharpDX\b", text):
        notes.append("still mentions SharpDX")

    return text, notes


def drop_duplicate_usings(text: str) -> str:
    lines = text.split("\n")
    seen = set()
    result = []

    for line in lines:
        stripped = line.strip()

        if stripped.startswith("using ") and stripped.endswith(";") and "=" not in stripped:
            if stripped in seen:
                continue

            seen.add(stripped)

        result.append(line)

    return "\n".join(result)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("paths", nargs="+", help="files or directories to rewrite")
    parser.add_argument("--dry-run", action="store_true", help="report what would change without writing")
    arguments = parser.parse_args()

    files = []

    for path in arguments.paths:
        candidate = pathlib.Path(path)
        files.extend(sorted(candidate.rglob("*.cs")) if candidate.is_dir() else [candidate])

    changed = 0
    flagged = []

    for file in files:
        # Generated interop and the facade itself are not migrated.
        if any(part in ("obj", "bin", ".temp") for part in file.parts):
            continue

        original = file.read_text()

        if "SharpDX" not in original:
            continue

        rewritten, notes = rewrite(original)

        if rewritten != original:
            changed += 1

            if not arguments.dry_run:
                file.write_text(rewritten)

        if notes:
            flagged.append((file, notes))

    print(f"{'Would rewrite' if arguments.dry_run else 'Rewrote'} {changed} files")

    if flagged:
        print(f"\n{len(flagged)} need a look by hand:")

        for file, notes in flagged:
            print(f"  {file}: {', '.join(notes)}")

    return 0


if __name__ == "__main__":
    sys.exit(main())
