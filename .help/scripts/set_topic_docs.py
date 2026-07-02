#!/usr/bin/env python3
"""
set_topic_docs.py — replace the doc body of each topic block in ui-topics.md.

Unlike fill_topic_docs.py (which only fills `_TODO_` placeholders), this overwrites the existing
body of every topic found in the drafts JSON — used to re-splice edited bodies (e.g. after adding
`[ui:Topic]` cross-reference links). Metadata (heading, id, synonyms, parent, classes) is preserved
verbatim; only the prose after the metadata's blank line, up to the next `## ` heading, is replaced.

    python set_topic_docs.py drafts.json
"""

import json
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
HELP = HERE.parent
MD = HELP / "references" / "topics" / "ui-topics.md"


def main():
    if len(sys.argv) < 2:
        sys.exit("usage: python set_topic_docs.py <drafts.json>")

    drafts = json.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
    lines = MD.read_text(encoding="utf-8").split("\n")

    out = []
    i = 0

    # Preamble before the first topic block.
    while i < len(lines) and not lines[i].startswith("## "):
        out.append(lines[i])
        i += 1

    replaced = []
    kept = []
    while i < len(lines):
        header = lines[i]           # "## <Term>"
        i += 1
        meta = []
        while i < len(lines) and lines[i].strip() != "" and not lines[i].startswith("## "):
            meta.append(lines[i])
            i += 1
        while i < len(lines) and lines[i].strip() == "":   # skip blank(s) after metadata
            i += 1
        body_lines = []
        while i < len(lines) and not lines[i].startswith("## "):
            body_lines.append(lines[i])
            i += 1

        topic_id = None
        for m in meta:
            mm = re.match(r"id:\s*(\S+)\s*$", m)
            if mm:
                topic_id = mm.group(1)

        original = "\n".join(body_lines).strip("\n")
        new_body = drafts.get(topic_id)
        if new_body and new_body.strip():
            body = new_body.rstrip("\n")
            replaced.append(topic_id)
        else:
            body = original
            kept.append(topic_id)

        out.append(header)
        out.extend(meta)
        out.append("")
        out.append(body)
        out.append("")

    # Collapse any trailing blank lines to a single newline at EOF.
    while len(out) > 1 and out[-1] == "" and out[-2] == "":
        out.pop()

    MD.write_text("\n".join(out).rstrip("\n") + "\n", encoding="utf-8")
    print(f"replaced {len(replaced)} body(ies); kept {len([k for k in kept if k])} unchanged")


if __name__ == "__main__":
    main()
