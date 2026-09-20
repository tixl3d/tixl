#!/usr/bin/env python3
"""Gives every constant buffer in a shader its own name.

FXC accepts two `cbuffer Params` blocks at different registers; Slang rejects the second as an ambiguous
reference, which stops the shader compiling for Vulkan. Nothing refers to a constant buffer by name - C#
binds by register and the shader graph replaces placeholders, not names - so renaming the later blocks is
safe, and it follows the names this codebase already uses: FloatParams for the block the shader graph fills
in, IntParams for one holding only integers.

    python3 Spikes/ShaderSurvey/rename_duplicate_cbuffers.py --dry-run Operators
    python3 Spikes/ShaderSurvey/rename_duplicate_cbuffers.py Operators
"""

import argparse
import pathlib
import re
import sys

HEADER = re.compile(r'cbuffer\s+(\w+)\s*:\s*register\s*\(\s*b(\d+)\s*\)')
DECLARATION = re.compile(r'^\s*(?:const\s+)?(\w+)\s+\w+\s*(?:\[[^\]]*\])?\s*;', re.M)
INTEGER_TYPES = {'int', 'uint', 'int2', 'int3', 'int4', 'uint2', 'uint3', 'uint4', 'bool'}


def body_of(text: str, start: int) -> str:
    """The block after a header, by brace matching - shader formatting is not consistent enough for a regex."""
    opening = text.find('{', start)

    if opening < 0:
        return ''

    depth = 0

    for index in range(opening, len(text)):
        if text[index] == '{':
            depth += 1
        elif text[index] == '}':
            depth -= 1

            if depth == 0:
                return text[opening + 1:index]

    return ''


def rename_in(text: str) -> tuple[str, list[tuple[str, str]]]:
    headers = [(match.start(), match.group(1), match.group(2)) for match in HEADER.finditer(text)]
    used = {name for _, name, _ in headers}
    seen = set()
    renames = []

    for start, name, register in headers:
        if name not in seen:
            seen.add(name)
            continue

        body = body_of(text, start)

        if 'FLOAT_PARAMS' in body:
            candidate = 'FloatParams'
        else:
            types = DECLARATION.findall(body)
            candidate = 'IntParams' if types and all(t in INTEGER_TYPES for t in types) else f'{name}2'

        # A name already in the file would only move the collision.
        index = 2
        while candidate in used:
            candidate = f'{name}{index}'
            index += 1

        used.add(candidate)
        renames.append((f'{name} : register(b{register})', f'{candidate} : register(b{register})'))

    for old, new in renames:
        text = text.replace('cbuffer ' + old, 'cbuffer ' + new, 1)

    return text, renames


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('paths', nargs='+')
    parser.add_argument('--dry-run', action='store_true')
    arguments = parser.parse_args()

    files = []

    for path in arguments.paths:
        candidate = pathlib.Path(path)
        files.extend(sorted(candidate.rglob('*.hlsl')) if candidate.is_dir() else [candidate])

    changed = 0
    total = 0

    for file in files:
        if any(part in ('obj', 'bin', '.temp') for part in file.parts):
            continue

        original = file.read_text(errors='ignore')
        updated, renames = rename_in(original)

        if not renames:
            continue

        changed += 1
        total += len(renames)
        print(f'{file}: ' + ', '.join(f'{old.split(" ")[0]} -> {new.split(" ")[0]} at b{new.split("(b")[1][0]}'
                                      for old, new in renames))

        if not arguments.dry_run:
            file.write_text(updated)

    print(f'\n{"Would rename" if arguments.dry_run else "Renamed"} {total} constant buffers in {changed} files')
    return 0


if __name__ == '__main__':
    sys.exit(main())
